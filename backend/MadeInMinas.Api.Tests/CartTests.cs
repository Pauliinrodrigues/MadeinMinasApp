using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class CartTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Category category = new() { Name = "Lanches", NormalizedName = "LANCHES" };
    private readonly Customer customer = new() { Name = "Maria", NormalizedName = "MARIA", Phone = "+5531999991234" };
    private Product burger = null!;
    private Product drink = null!;
    private Address address = null!;

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.RecipeItems.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
        burger = new Product { Category = category, Name = "Uai Sô", NormalizedName = "UAI SÔ", Price = 29.90m, IsActive = true, IsAvailable = true };
        drink = new Product { Category = category, Name = "Suco", NormalizedName = "SUCO", Price = 5.15m, IsActive = true, IsAvailable = true };
        address = new Address { CustomerId = customer.Id, Street = "Rua A", Number = "S/N", Neighborhood = "Centro", City = "Belo Horizonte", State = "MG", Complement = "Casa", Reference = "Portão verde", PostalCode = "30110000" };
        database.Products.AddRange(burger, drink);
        database.Customers.Add(customer);
        database.Addresses.Add(address);
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => Task.CompletedTask;

    private CartQuoteRequest Input() => new(customer.Id, "Pickup", [new(burger.Id, 2, " Sem cebola "), new(drink.Id, 3)], Notes: " Embalar separado ");

    private async Task<HttpClient> SignInAsync(int roleId = 1)
    {
        var user = await factory.CreateUserAsync(roleId);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AnonymousKitchenAndDispatchCannotUseCart(int roleId)
    {
        using var client = roleId == 0 ? factory.CreateStaffClient() : await SignInAsync(roleId);
        var status = roleId == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(status, (await client.GetAsync("/api/cart/products")).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync("/api/cart/quote", Input())).StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StaffCanReviewUsingDatabasePricesWithoutChangingRecords(int roleId)
    {
        using var client = await SignInAsync(roleId);
        var list = await client.GetAsync("/api/cart/products");
        list.EnsureSuccessStatusCode();
        Assert.True(list.Headers.CacheControl?.NoStore);
        var catalog = (await list.Content.ReadFromJsonAsync<CartProductPageResponse>())!;
        Assert.Equal(2, catalog.TotalCount);
        var response = await client.PostAsJsonAsync("/api/cart/quote", Input());
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var quote = (await response.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        Assert.Equal(75.25m, quote.Subtotal);
        Assert.Equal(59.80m, quote.Items[0].LineTotal);
        Assert.Equal(29.90m, quote.Items[0].UnitPrice);
        Assert.Equal("Uai Sô", quote.Items[0].Name);
        Assert.Equal("Sem cebola", quote.Items[0].Notes);
        Assert.Equal("Embalar separado", quote.Notes);
        Assert.Equal(customer.Phone, quote.Customer.Phone);
        Assert.Equal("Pickup", quote.Fulfillment);
        Assert.Null(quote.Address);
        Assert.InRange(quote.CalculatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        if (roleId == 2)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/products")).StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(2, await database.Products.CountAsync());
            Assert.Equal(burger.UpdatedAt, (await database.Products.SingleAsync(product => product.Id == burger.Id)).UpdatedAt);
            Assert.Equal(1, await database.Customers.CountAsync());
            Assert.Equal(1, await database.Addresses.CountAsync());
        });
    }

    [Fact]
    public async Task DeliveryUsesCurrentAddressAndCustomerFromDatabase()
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Customers.Where(item => item.Id == customer.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.Name, "Maria Silva"));
            await database.Addresses.Where(item => item.Id == address.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.Number, "25B"));
        });
        var response = await client.PostAsJsonAsync("/api/cart/quote", Input() with { Fulfillment = "Delivery", AddressId = address.Id });
        response.EnsureSuccessStatusCode();
        var quote = (await response.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        Assert.Equal("Maria Silva", quote.Customer.Name);
        Assert.Equal("Delivery", quote.Fulfillment);
        Assert.Equal("25B", quote.Address!.Number);
        Assert.Equal(address.Reference, quote.Address.Reference);
        Assert.Equal(address.PostalCode, quote.Address.PostalCode);
    }

    [Fact]
    public async Task NewReviewReflectsPriceChangesAndKeepsSeparateItemNotes()
    {
        using var client = await SignInAsync();
        var input = Input() with { Items = [new(burger.Id, 1, "Sem cebola"), new(burger.Id, 2, "Bem passado")], Notes = " " };
        var first = await client.PostAsJsonAsync("/api/cart/quote", input);
        Assert.Equal(89.70m, (await first.Content.ReadFromJsonAsync<CartQuoteResponse>())!.Subtotal);
        await factory.WithDatabaseAsync(async database =>
            await database.Products.Where(item => item.Id == burger.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 31.01m)));
        var second = await client.PostAsJsonAsync("/api/cart/quote", input);
        var quote = (await second.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        Assert.Equal(93.03m, quote.Subtotal);
        Assert.Equal(2, quote.Items.Length);
        Assert.Equal("Bem passado", quote.Items[1].Notes);
        Assert.Null(quote.Notes);
    }

    [Theory]
    [InlineData("product")]
    [InlineData("availability")]
    [InlineData("category")]
    [InlineData("missing")]
    public async Task UnavailableProductsAreHiddenAndCannotBeQuoted(string change)
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            if (change == "category")
                await database.Categories.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            else if (change == "product")
                await database.Products.Where(item => item.Id == burger.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            else if (change == "availability")
                await database.Products.Where(item => item.Id == burger.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsAvailable, false));
            else
                await database.Products.Where(item => item.Id == burger.Id).ExecuteDeleteAsync();
        });
        var catalog = (await client.GetFromJsonAsync<CartProductPageResponse>("/api/cart/products?isActive=false"))!;
        Assert.DoesNotContain(catalog.Items, item => item.Id == burger.Id);
        await ProblemAsync(await client.PostAsJsonAsync("/api/cart/quote", Input()), "CartProductUnavailable");
    }

    [Fact]
    public async Task CatalogSupportsSearchCategoryAndPagination()
    {
        using var client = await SignInAsync();
        var first = (await client.GetFromJsonAsync<CartProductPageResponse>("/api/cart/products?pageSize=1"))!;
        var second = (await client.GetFromJsonAsync<CartProductPageResponse>("/api/cart/products?pageSize=1&page=2"))!;
        Assert.Equal(2, first.TotalCount);
        Assert.Equal("Suco", Assert.Single(first.Items).Name);
        Assert.Equal(burger.Id, Assert.Single(second.Items).Id);
        var found = (await client.GetFromJsonAsync<CartProductPageResponse>($"/api/cart/products?search=%20uai%20&categoryId={category.Id}"))!;
        Assert.Equal(burger.Id, Assert.Single(found.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<CartProductPageResponse>($"/api/cart/products?categoryId={Guid.NewGuid()}"))!.Items);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=1000001")]
    [InlineData("categoryId=invalid")]
    public async Task InvalidCatalogQueriesAreRejected(string query)
    {
        using var client = await SignInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/cart/products?" + query)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomerMustExistAndBeActive(bool missing)
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database => await database.Customers.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false)));
        await ProblemAsync(await client.PostAsJsonAsync("/api/cart/quote", Input() with { CustomerId = missing ? Guid.NewGuid() : customer.Id }), "CartCustomerUnavailable");
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("another-customer")]
    public async Task DeliveryAddressMustBeActiveAndBelongToCustomer(string change)
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            if (change == "inactive")
                await database.Addresses.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            if (change == "another-customer")
            {
                var other = new Customer { Name = "Outro", NormalizedName = "OUTRO", Phone = "+5531988881234" };
                database.Customers.Add(other);
                await database.SaveChangesAsync();
                await database.Addresses.ExecuteUpdateAsync(set => set.SetProperty(item => item.CustomerId, other.Id));
            }
        });
        await ProblemAsync(await client.PostAsJsonAsync("/api/cart/quote", Input() with { Fulfillment = "Delivery", AddressId = change == "missing" ? Guid.NewGuid() : address.Id }), "CartAddressUnavailable");
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedBeforeCalculation()
    {
        using var client = await SignInAsync();
        var invalid = new[]
        {
            Input() with { CustomerId = null }, Input() with { CustomerId = Guid.Empty },
            Input() with { Fulfillment = "Unknown" }, Input() with { Fulfillment = "Delivery" },
            Input() with { AddressId = address.Id }, Input() with { Items = [] }, Input() with { Items = null! },
            Input() with { Items = [null!] }, Input() with { Items = [new(null, 1)] },
            Input() with { Items = [new(Guid.Empty, 1)] }, Input() with { Items = [new(burger.Id, null)] },
            Input() with { Items = [new(burger.Id, 0)] }, Input() with { Items = [new(burger.Id, -1)] },
            Input() with { Items = [new(burger.Id, 100)] }, Input() with { Items = [new(burger.Id, 50), new(burger.Id, 50)] },
            Input() with { Items = Enumerable.Repeat(new CartItemRequest(burger.Id, 1), 51).ToArray() },
            Input() with { Notes = new string('a', 501) }, Input() with { Items = [new(burger.Id, 1, new string('a', 251))] }
        };
        foreach (var request in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/cart/quote", request)).StatusCode);
        var fractional = JsonSerializer.SerializeToNode(Input(), JsonSerializerOptions.Web)!;
        fractional["items"]![0]!["quantity"] = 1.5m;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/cart/quote", fractional)).StatusCode);
    }

    [Theory]
    [InlineData("total", false)]
    [InlineData("discount", false)]
    [InlineData("deliveryFee", false)]
    [InlineData("unitPrice", true)]
    [InlineData("name", true)]
    public async Task ClientCannotSupplyCommercialValuesOrNames(string field, bool nested)
    {
        using var client = await SignInAsync();
        var input = JsonSerializer.SerializeToNode(Input(), JsonSerializerOptions.Web)!;
        var target = nested ? input["items"]![0]! : input;
        target[field] = JsonValue.Create(0);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/cart/quote", input)).StatusCode);
    }

    [Fact]
    public async Task MaximumValidQuantityAndPriceDoNotOverflow()
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database => await database.Products.Where(item => item.Id == burger.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 999999.99m)));
        var response = await client.PostAsJsonAsync("/api/cart/quote", Input() with { Items = [new(burger.Id, 99)] });
        response.EnsureSuccessStatusCode();
        Assert.Equal(98999999.01m, (await response.Content.ReadFromJsonAsync<CartQuoteResponse>())!.Subtotal);
    }

    [Fact]
    public async Task RevokedSessionCannotReadCatalogOrQuote()
    {
        using var client = await SignInAsync(2);
        await factory.WithDatabaseAsync(async database => await database.Users.ExecuteUpdateAsync(set => set.SetProperty(item => item.SecurityStamp, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cart/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/cart/quote", Input())).StatusCode);
    }
}
