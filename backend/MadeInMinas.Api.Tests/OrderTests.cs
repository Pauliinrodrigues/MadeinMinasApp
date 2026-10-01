using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class OrderTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Customer customer = new() { Name = "Maria", NormalizedName = "MARIA", Phone = "+5531999991234" };
    private readonly Category category = new() { Name = "Lanches", NormalizedName = "LANCHES" };
    private Product burger = null!;
    private Address address = null!;

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.Orders.ExecuteDeleteAsync();
        await database.RecipeItems.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
        burger = new Product { Category = category, Name = "Uai Sô", NormalizedName = "UAI SÔ", Price = 29.90m, IsActive = true, IsAvailable = true };
        address = new Address { CustomerId = customer.Id, Street = "Rua A", Number = "10", Neighborhood = "Centro", City = "Belo Horizonte", State = "MG" };
        database.Products.Add(burger);
        database.Customers.Add(customer);
        database.Addresses.Add(address);
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => factory.WithDatabaseAsync(async database => await database.Orders.ExecuteDeleteAsync());
    private CartQuoteRequest Cart(bool delivery = false) => new(customer.Id, delivery ? "Delivery" : "Pickup",
        [new(burger.Id, 2, " Sem cebola ")], delivery ? address.Id : null, " Embalar separado ")
    { DeliveryFee = delivery ? 4.50m : 0 };

    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    private async Task<CreateOrderRequest> ReviewAsync(HttpClient client, bool delivery = false)
    {
        var input = Cart(delivery);
        var response = await client.PostAsJsonAsync("/api/cart/quote", input);
        response.EnsureSuccessStatusCode();
        var quote = (await response.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        return new CreateOrderRequest(Guid.NewGuid(), quote.ReviewToken, input);
    }

    private async Task<OrderResponse> CreateAsync(HttpClient client, bool delivery = false)
    {
        var response = await client.PostAsJsonAsync("/api/orders", await ReviewAsync(client, delivery));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.Conflict)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlySalesStaffCanUseOrders(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var status = role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(status, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(status, (await client.GetAsync($"/api/orders/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync("/api/orders", new { })).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync($"/api/orders/{Guid.NewGuid()}/status", new { })).StatusCode);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task CreationStoresSnapshotsAndHistory(int role, bool delivery)
    {
        using var client = await SignInAsync(role);
        var request = await ReviewAsync(client, delivery);
        var response = await client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.True(order.Number > 0);
        Assert.Equal("Manual", order.Origin);
        Assert.Equal("New", order.Status);
        Assert.Equal(1, order.Version);
        Assert.Equal(59.80m, order.Subtotal);
        Assert.Equal(delivery ? 64.30m : 59.80m, order.Total);
        Assert.Equal("Sem cebola", Assert.Single(order.Items).Notes);
        Assert.Equal("Embalar separado", order.Notes);
        var history = Assert.Single(order.History);
        Assert.Null(history.FromStatus);
        Assert.Equal("New", history.ToStatus);
        Assert.Equal(order.CreatedAt, history.OccurredAt);
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 40m).SetProperty(item => item.Name, "Outro nome"));
            await database.Customers.ExecuteUpdateAsync(set => set.SetProperty(item => item.Name, "Maria alterada"));
            await database.Addresses.ExecuteUpdateAsync(set => set.SetProperty(item => item.Street, "Rua B"));
        });
        var saved = (await client.GetFromJsonAsync<OrderResponse>(response.Headers.Location))!;
        Assert.Equal("Maria", saved.Customer.Name);
        Assert.Equal("Uai Sô", saved.Items[0].Name);
        Assert.Equal(29.90m, saved.Items[0].UnitPrice);
        if (delivery)
            Assert.Equal("Rua A", saved.Address!.Street);
        else
            Assert.Null(saved.Address);
        var confirmed = await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", 1));
        confirmed.EnsureSuccessStatusCode();
        Assert.Equal(order.Total, (await confirmed.Content.ReadFromJsonAsync<OrderResponse>())!.Total);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("customer")]
    [InlineData("address")]
    [InlineData("fee")]
    public async Task ChangedReviewCannotBeSaved(string change)
    {
        using var client = await SignInAsync();
        var request = await ReviewAsync(client, true);
        await factory.WithDatabaseAsync(async database =>
        {
            if (change == "price")
                await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 30m));
            if (change == "customer")
                await database.Customers.ExecuteUpdateAsync(set => set.SetProperty(item => item.Phone, "+5531988881234"));
            if (change == "address")
                await database.Addresses.ExecuteUpdateAsync(set => set.SetProperty(item => item.Number, "11"));
        });
        if (change == "fee")
            request = request with { Cart = request.Cart with { DeliveryFee = 5 } };
        await ProblemAsync(await client.PostAsJsonAsync("/api/orders", request), "OrderReviewChanged");
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.Orders.ToArrayAsync()));
    }

    [Theory]
    [InlineData("product", "CartProductUnavailable")]
    [InlineData("category", "CartProductUnavailable")]
    [InlineData("customer", "CartCustomerUnavailable")]
    [InlineData("address", "CartAddressUnavailable")]
    public async Task AvailabilityIsCheckedAtCreationAndConfirmation(string change, string code)
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client, true);
        var request = await ReviewAsync(client, true);
        await factory.WithDatabaseAsync(async database =>
        {
            if (change == "product")
                await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsAvailable, false));
            if (change == "category")
                await database.Categories.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            if (change == "customer")
                await database.Customers.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            if (change == "address")
                await database.Addresses.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        });
        await ProblemAsync(await client.PostAsJsonAsync("/api/orders", request), code);
        await ProblemAsync(await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", 1)), code);
        var saved = (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        Assert.Equal("New", saved.Status);
        Assert.Single(saved.History);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", 1, "Indisponível"))).StatusCode);
    }

    [Fact]
    public async Task ConcurrentReplaysCreateOnlyOneOrderAndSurviveLaterChanges()
    {
        using var client = await SignInAsync();
        var input = await ReviewAsync(client);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/orders", input)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var orders = await Task.WhenAll(responses.Select(async response => (await response.Content.ReadFromJsonAsync<OrderResponse>())!));
        Assert.Single(orders.Select(order => order.Id).Distinct());
        await factory.WithDatabaseAsync(async database => await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsAvailable, false)));
        var replay = await client.PostAsJsonAsync("/api/orders", input);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(orders[0].Id, (await replay.Content.ReadFromJsonAsync<OrderResponse>())!.Id);
        (await client.PutAsJsonAsync($"/api/orders/{orders[0].Id}/status", new OrderStatusRequest("Cancelled", 1, "Cliente desistiu"))).EnsureSuccessStatusCode();
        var cancelledReplay = await client.PostAsJsonAsync("/api/orders", input);
        Assert.Equal(HttpStatusCode.OK, cancelledReplay.StatusCode);
        Assert.Equal("Cancelled", (await cancelledReplay.Content.ReadFromJsonAsync<OrderResponse>())!.Status);
        await ProblemAsync(await client.PostAsJsonAsync("/api/orders", input with { Cart = input.Cart with { Notes = "Diferente" } }), "OrderRequestConflict");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(1, await database.Orders.CountAsync());
            Assert.Equal(2, await database.OrderStatusHistory.CountAsync());
        });
    }

    [Fact]
    public async Task RequestKeysAreScopedToActorAndNumbersAreUnique()
    {
        using var admin = await SignInAsync();
        using var attendant = await SignInAsync(2);
        var input = await ReviewAsync(admin);
        var responses = await Task.WhenAll(admin.PostAsJsonAsync("/api/orders", input), attendant.PostAsJsonAsync("/api/orders", input),
            admin.PostAsJsonAsync("/api/orders", input with { RequestId = Guid.NewGuid() }));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var orders = await Task.WhenAll(responses.Select(async response => (await response.Content.ReadFromJsonAsync<OrderResponse>())!));
        Assert.Equal(3, orders.Select(order => order.Number).Distinct().Count());
    }

    [Fact]
    public async Task StatusRulesPreserveHistoryAndRequireAdminForConfirmedCancellation()
    {
        using var attendant = await SignInAsync(2);
        using var admin = await SignInAsync();
        var order = await CreateAsync(attendant);
        var path = $"/api/orders/{order.Id}/status";
        var confirm = new OrderStatusRequest("Confirmed", 1);
        Assert.Equal(HttpStatusCode.OK, (await attendant.PutAsJsonAsync(path, confirm)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await attendant.PutAsJsonAsync(path, confirm)).StatusCode);
        await ProblemAsync(await attendant.PutAsJsonAsync(path, new OrderStatusRequest("Cancelled", 2, "Cliente desistiu")), "OrderCancellationDenied", HttpStatusCode.Forbidden);
        await ProblemAsync(await admin.PutAsJsonAsync(path, new OrderStatusRequest("Cancelled", 1, "Cliente desistiu")), "OrderVersionConflict");
        var cancel = new OrderStatusRequest("Cancelled", 2, " Cliente desistiu ");
        var response = await admin.PutAsJsonAsync(path, cancel);
        response.EnsureSuccessStatusCode();
        var cancelled = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(3, cancelled.Version);
        Assert.Equal(new[] { "New", "Confirmed", "Cancelled" }, cancelled.History.Select(history => history.ToStatus));
        Assert.Equal("Cliente desistiu", cancelled.History[2].Reason);
        Assert.NotEqual(cancelled.History[1].ActorId, cancelled.History[2].ActorId);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path, cancel)).StatusCode);
        await ProblemAsync(await admin.PutAsJsonAsync(path, new OrderStatusRequest("Confirmed", 3)), "OrderTransitionDenied");
        Assert.Equal(3, (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History.Length);
    }

    [Fact]
    public async Task ConcurrentStatusChangesAllowOnlyOneTransition()
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client);
        var path = $"/api/orders/{order.Id}/status";
        var responses = await Task.WhenAll(client.PutAsJsonAsync(path, new OrderStatusRequest("Confirmed", 1)),
            client.PutAsJsonAsync(path, new OrderStatusRequest("Cancelled", 1, "Desistiu")));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History.Length);
    }

    [Fact]
    public async Task ListSupportsSearchStatusCustomerAndPagination()
    {
        using var client = await SignInAsync();
        var first = await CreateAsync(client);
        var second = await CreateAsync(client);
        (await client.PutAsJsonAsync($"/api/orders/{second.Id}/status", new OrderStatusRequest("Cancelled", 1, "Duplicado"))).EnsureSuccessStatusCode();
        var page = (await client.GetFromJsonAsync<OrderPageResponse>("/api/orders?pageSize=1"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.Equal(first.Id, Assert.Single((await client.GetFromJsonAsync<OrderPageResponse>("/api/orders?pageSize=1&page=2"))!.Items).Id);
        Assert.Equal(first.Id, Assert.Single((await client.GetFromJsonAsync<OrderPageResponse>($"/api/orders?status=New&customerId={customer.Id}&search=maria"))!.Items).Id);
        Assert.Equal(second.Id, Assert.Single((await client.GetFromJsonAsync<OrderPageResponse>($"/api/orders?search=%23{second.Number}"))!.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<OrderPageResponse>($"/api/orders?customerId={Guid.NewGuid()}"))!.Items);
        await ProblemAsync(await client.GetAsync($"/api/orders/{Guid.NewGuid()}"), "OrderNotFound", HttpStatusCode.NotFound);
        foreach (var query in new[] { "page=0", "pageSize=101", "status=Delivered", "customerId=wrong" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?" + query)).StatusCode);
    }

    [Fact]
    public async Task InvalidOrInjectedCommercialDataCannotCreateOrChangeOrders()
    {
        using var client = await SignInAsync();
        var input = await ReviewAsync(client);
        foreach (var invalid in new[] { input with { RequestId = null }, input with { RequestId = Guid.Empty },
            input with { ReviewToken = "" }, input with { Cart = null! }, input with { Cart = input.Cart with { Items = [null!] } } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", invalid)).StatusCode);
        foreach (var field in new[] { "total", "status", "number", "origin", "createdById" })
        {
            var json = JsonSerializer.SerializeToNode(input, JsonSerializerOptions.Web)!;
            json[field] = "forged";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", json)).StatusCode);
        }
        var order = await CreateAsync(client);
        foreach (var request in new[] { new OrderStatusRequest("Cancelled", 1), new("Cancelled", 1, " "),
            new("Confirmed", null), new("Confirmed", 0), new("Delivered", 1), new("Confirmed", 1, "Unexpected") })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", request)).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History);
    }

    [Fact]
    public async Task RevokedActorCannotWriteThroughService()
    {
        using var client = await SignInAsync();
        var input = await ReviewAsync(client);
        var actor = await factory.CreateUserAsync(2);
        await factory.WithDatabaseAsync(async database => await database.Users.Where(user => user.Id == actor.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.SecurityStamp, Guid.NewGuid())));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<OrderService>();
        var exception = await Assert.ThrowsAsync<OrderException>(() => service.CreateAsync(actor.Id, actor.SecurityStamp, input, CancellationToken.None));
        Assert.Equal(OrderError.InvalidSession, exception.Error);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ServiceRechecksCurrentRole(int role)
    {
        using var client = await SignInAsync();
        var input = await ReviewAsync(client);
        var actor = await factory.CreateUserAsync(role);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<OrderService>();
        var exception = await Assert.ThrowsAsync<OrderException>(() => service.CreateAsync(actor.Id, actor.SecurityStamp, input, CancellationToken.None));
        Assert.Equal(OrderError.PermissionDenied, exception.Error);
    }

    [Fact]
    public async Task DatabaseRejectsInconsistentAmountsAndDeletionOfReferencedData()
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client, true);
        await factory.WithDatabaseAsync(async database =>
        {
            var total = await Assert.ThrowsAsync<PostgresException>(() => database.Orders.Where(item => item.Id == order.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Total, 1m)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, total.SqlState);
            var line = await Assert.ThrowsAsync<PostgresException>(() => database.OrderItems.Where(item => item.OrderId == order.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Quantity, 100)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, line.SqlState);
            var product = await Assert.ThrowsAsync<PostgresException>(() => database.Products.Where(item => item.Id == burger.Id).ExecuteDeleteAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, product.SqlState);
            var destination = await Assert.ThrowsAsync<PostgresException>(() => database.Addresses.Where(item => item.Id == address.Id).ExecuteDeleteAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, destination.SqlState);
        });
        var saved = (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        Assert.Equal(64.30m, saved.Total);
        Assert.Equal(2, Assert.Single(saved.Items).Quantity);
    }

    [Fact]
    public async Task MaximumPriceAndQuantityCanBePersistedWithoutRounding()
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database => await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 999999.99m)));
        var input = Cart(true) with { Items = [new(burger.Id, 99)], DeliveryFee = 9999.99m };
        var response = await client.PostAsJsonAsync("/api/cart/quote", input);
        response.EnsureSuccessStatusCode();
        var quote = (await response.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        var saved = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(), quote.ReviewToken, input));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        Assert.Equal(99009999m, (await saved.Content.ReadFromJsonAsync<OrderResponse>())!.Total);
    }
}
