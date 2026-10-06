using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.PublicCart;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class PublicCartTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private const string Path = "/api/public-cart/quote";
    private readonly Category category = new() { Name = "Lanches", NormalizedName = "LANCHES" };
    private Product burger = null!;
    private Product drink = null!;

    private static async Task ClearAsync(AppDbContext database)
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    }

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await ClearAsync(database);
        burger = new Product { Name = "Uai Sô", NormalizedName = "UAI SÔ", Category = category, Price = 29.90m, IsActive = true, IsAvailable = true };
        drink = new Product { Name = "Suco", NormalizedName = "SUCO", Category = category, Price = 5.15m, IsActive = true, IsAvailable = true };
        database.Products.AddRange(burger, drink);
        var ingredient = new Ingredient { Name = "Custo privado", NormalizedName = "CUSTO PRIVADO", Unit = "un", UnitCost = 18.1234m, CurrentStock = 3m };
        database.Recipes.AddRange(
            new Recipe { Product = burger, YieldQuantity = 1, Items = [new RecipeItem { Ingredient = ingredient, Quantity = 0.001m }] },
            new Recipe { Product = drink, YieldQuantity = 1, Items = [new RecipeItem { Ingredient = ingredient, Quantity = 0.001m }] });
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    private PublicCartQuoteRequest Input() => new([new(burger.Id, 2, "  Sem cebola  "), new(drink.Id, 3)], "  Embalar separado  ");

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("stock")]
    public async Task ProductionConstraintsRejectQuoteWithoutConsumingStock(string condition)
    {
        await factory.WithDatabaseAsync(async database =>
        {
            if (condition == "missing")
                await database.Recipes.Where(recipe => recipe.ProductId == burger.Id).ExecuteDeleteAsync();
            if (condition == "inactive")
                await database.Ingredients.ExecuteUpdateAsync(update => update.SetProperty(item => item.IsActive, false));
            if (condition == "stock")
                await database.Ingredients.ExecuteUpdateAsync(update => update.SetProperty(item => item.CurrentStock, 0.004m));
        });
        using var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync(Path, Input());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(condition == "stock" ? 0.004m : 3m, (await database.Ingredients.SingleAsync()).CurrentStock);
            Assert.False(await database.StockMovements.AnyAsync());
            Assert.False(await database.Orders.AnyAsync());
        });
    }

    [Fact]
    public async Task SharedIngredientsAndSeparateNotesUseTheSameRoundingAsConfirmation()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Recipes.ExecuteUpdateAsync(update => update.SetProperty(recipe => recipe.YieldQuantity, 3));
            await database.Ingredients.ExecuteUpdateAsync(update => update.SetProperty(item => item.CurrentStock, 0.002m));
        });
        using var client = factory.CreateStaffClient();
        // Three burger lines together consume .001; the drink consumes another .001.
        var input = new PublicCartQuoteRequest([new(burger.Id, 1, "A"), new(burger.Id, 2, "B"), new(drink.Id, 1)]);
        await ReadAsync(await client.PostAsJsonAsync(Path, input));
        input = input with { Items = [new(burger.Id, 2, "A"), new(burger.Id, 2, "B"), new(drink.Id, 1)] };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Path, input)).StatusCode);
    }

    private static async Task<PublicCartQuoteResponse> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<PublicCartQuoteResponse>())!;
    }

    [Fact]
    public async Task AnonymousQuoteUsesDecimalDatabasePricesAndNormalizesNotesWithoutWriting()
    {
        using var client = factory.CreateStaffClient();
        var quote = await ReadAsync(await client.PostAsJsonAsync(Path, Input()));
        Assert.Equal(75.25m, quote.Subtotal);
        Assert.Equal(59.80m, quote.Items[0].LineTotal);
        Assert.Equal(15.45m, quote.Items[1].LineTotal);
        Assert.Equal(29.90m, quote.Items[0].UnitPrice);
        Assert.Equal("Sem cebola", quote.Items[0].Notes);
        Assert.Equal("Embalar separado", quote.Notes);
        Assert.Equal([burger.Id, drink.Id], quote.Items.Select(item => item.ProductId));
        Assert.InRange(quote.CalculatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        await ReadAsync(await client.PostAsJsonAsync(Path, Input()));
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(2, await database.Products.CountAsync());
            Assert.Equal(3m, (await database.Ingredients.SingleAsync()).CurrentStock);
            Assert.Equal(18.1234m, (await database.Ingredients.SingleAsync()).UnitCost);
            Assert.False(await database.Customers.AnyAsync());
            Assert.False(await database.Addresses.AnyAsync());
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.Payments.AnyAsync());
            Assert.False(await database.StockMovements.AnyAsync());
        });
    }

    [Fact]
    public async Task QuoteContainsOnlyCommercialFieldsAndNoCheckoutOrCostData()
    {
        using var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync(Path, Input());
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["calculatedAt", "items", "notes", "subtotal"], json.RootElement.EnumerateObject().Select(field => field.Name).Order());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            Assert.Equal(["lineTotal", "name", "notes", "productId", "quantity", "unitPrice"], item.EnumerateObject().Select(field => field.Name).Order());
    }

    [Fact]
    public async Task SeparateNotesPreserveLinesWithAnAggregateOf99Units()
    {
        using var client = factory.CreateStaffClient();
        var input = new PublicCartQuoteRequest([new(burger.Id, 98, "Sem molho"), new(burger.Id, 1, "Com molho")], "   ");
        var quote = await ReadAsync(await client.PostAsJsonAsync(Path, input));
        Assert.Equal(2, quote.Items.Length);
        Assert.Equal("Com molho", quote.Items[1].Notes);
        Assert.Equal(2960.10m, quote.Subtotal);
        Assert.Null(quote.Notes);
    }

    [Fact]
    public async Task FiftyLinesAndMaximumNotesAreAccepted()
    {
        using var client = factory.CreateStaffClient();
        var input = new PublicCartQuoteRequest(Enumerable.Range(0, 50).Select(_ => new CartItemRequest(burger.Id, 1, new string('a', 250))).ToArray(), new string('b', 500));
        var quote = await ReadAsync(await client.PostAsJsonAsync(Path, input));
        Assert.Equal(50, quote.Items.Length);
        Assert.Equal(1495m, quote.Subtotal);
    }

    [Fact]
    public async Task RepeatedQuoteReflectsCurrentNamePriceAndKeepsCentPrecision()
    {
        using var client = factory.CreateStaffClient();
        await ReadAsync(await client.PostAsJsonAsync(Path, Input()));
        await factory.WithDatabaseAsync(async database =>
            await database.Products.Where(product => product.Id == burger.Id).ExecuteUpdateAsync(update => update
                .SetProperty(product => product.Price, 0.01m).SetProperty(product => product.Name, "Nome atualizado")));
        var quote = await ReadAsync(await client.PostAsJsonAsync(Path, Input()));
        Assert.Equal("Nome atualizado", quote.Items[0].Name);
        Assert.Equal(0.02m, quote.Items[0].LineTotal);
        Assert.Equal(15.47m, quote.Subtotal);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("inactive")]
    [InlineData("category")]
    [InlineData("unknown")]
    public async Task UnavailableProductRejectsWholeQuoteWithoutDisclosingItsData(string condition)
    {
        var input = Input();
        await factory.WithDatabaseAsync(async database =>
        {
            if (condition == "paused")
                await database.Products.Where(product => product.Id == burger.Id).ExecuteUpdateAsync(update => update.SetProperty(product => product.IsAvailable, false));
            if (condition == "inactive")
                await database.Products.Where(product => product.Id == burger.Id).ExecuteUpdateAsync(update => update.SetProperty(product => product.IsActive, false));
            if (condition == "category")
                await database.Categories.ExecuteUpdateAsync(update => update.SetProperty(item => item.IsActive, false));
        });
        if (condition == "unknown")
            input = input with { Items = [new(Guid.NewGuid(), 1), new(drink.Id, 1)] };
        using var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync(Path, input);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CartProductUnavailable", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("subtotal", out _));
        Assert.DoesNotContain(burger.Name, json.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("fraction")]
    [InlineData("over99")]
    [InlineData("quantityNull")]
    [InlineData("productNull")]
    [InlineData("emptyGuid")]
    [InlineData("nullItems")]
    [InlineData("emptyItems")]
    [InlineData("nullItem")]
    [InlineData("over50")]
    [InlineData("aggregate")]
    [InlineData("itemNotes")]
    [InlineData("generalNotes")]
    [InlineData("unitPrice")]
    [InlineData("subtotal")]
    [InlineData("customerId")]
    [InlineData("deliveryFee")]
    public async Task InvalidOrTamperedInputsAreRejected(string condition)
    {
        var input = JsonSerializer.SerializeToNode(Input(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var first = input["items"]![0]!;
        switch (condition)
        {
            case "zero":
                first["quantity"] = 0;
                break;
            case "fraction":
                first["quantity"] = 1.5m;
                break;
            case "over99":
                first["quantity"] = 100;
                break;
            case "quantityNull":
                first["quantity"] = null;
                break;
            case "productNull":
                first["productId"] = null;
                break;
            case "emptyGuid":
                first["productId"] = Guid.Empty.ToString();
                break;
            case "nullItems":
                input["items"] = null;
                break;
            case "emptyItems":
                input["items"] = new JsonArray();
                break;
            case "nullItem":
                input["items"] = new JsonArray((JsonNode?)null);
                break;
            case "over50":
                input["items"] = new JsonArray(Enumerable.Range(0, 51).Select(_ => { var item = first.DeepClone(); item["quantity"] = 1; return item; }).ToArray());
                break;
            case "aggregate":
                first["quantity"] = 99;
                input["items"]![1]!["productId"] = burger.Id.ToString();
                break;
            case "itemNotes":
                first["notes"] = new string('a', 251);
                break;
            case "generalNotes":
                input["notes"] = new string('b', 501);
                break;
            case "unitPrice":
                first["unitPrice"] = 0.01m;
                break;
            case "subtotal":
                input["subtotal"] = 0.01m;
                break;
            case "customerId":
                input["customerId"] = Guid.NewGuid().ToString();
                break;
            case "deliveryFee":
                input["deliveryFee"] = 0;
                break;
        }
        using var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync(Path, input);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PublicQuoteDoesNotRequireStaffSessionAndStaffRoutesRemainProtected()
    {
        using var client = factory.CreateStaffClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        await ReadAsync(await client.PostAsJsonAsync(Path, Input()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/cart/quote", Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task StaffAndPublicQuotesUseTheSameProductPricesAndSubtotals()
    {
        var customer = new Customer { Name = "Teste", NormalizedName = "TESTE", Phone = "+5531999991234" };
        await factory.WithDatabaseAsync(async database => { database.Customers.Add(customer); await database.SaveChangesAsync(); });
        var user = await factory.CreateUserAsync(2);
        using var client = factory.CreateStaffClient();
        var publicQuote = await ReadAsync(await client.PostAsJsonAsync(Path, Input()));
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        var response = await client.PostAsJsonAsync("/api/cart/quote", new CartQuoteRequest(customer.Id, "Pickup", Input().Items, Notes: Input().Notes));
        response.EnsureSuccessStatusCode();
        var staffQuote = (await response.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        Assert.Equal(publicQuote.Items, staffQuote.Items);
        Assert.Equal(publicQuote.Subtotal, staffQuote.Subtotal);
    }
}
