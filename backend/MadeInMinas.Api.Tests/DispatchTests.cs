using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Kitchen;
using MadeInMinas.Api.DTOs.Dispatch;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.DTOs.Payments;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class DispatchTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Customer customer = new() { Name = "Cliente reservado", NormalizedName = "CLIENTE RESERVADO", Phone = "+5531999991234" };
    private Product product = null!;
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.RecipeItems.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
        product = new Product
        {
            Name = "Uai Sô",
            NormalizedName = "UAI SÔ",
            Price = 29.90m,
            IsActive = true,
            IsAvailable = true,
            Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" }
        };
        database.Products.Add(product);
        OrderStockFixture.AddRecipe(database, product);
        database.Customers.Add(customer);
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
    });
    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }
    private async Task<OrderResponse> ReadyAsync(HttpClient client, bool delivery = false)
    {
        Guid? addressId = null;
        if (delivery)
            await factory.WithDatabaseAsync(async database =>
            {
                var address = new Address { CustomerId = customer.Id, Street = "Rua histórica", Number = "12", Neighborhood = "Centro", City = "Belo Horizonte", State = "MG" };
                database.Addresses.Add(address);
                await database.SaveChangesAsync();
                addressId = address.Id;
            });
        var cart = new CartQuoteRequest(customer.Id, delivery ? "Delivery" : "Pickup", [new(product.Id, 2, "Sem cebola")], addressId, "Embalar separado") { DeliveryFee = delivery ? 5m : 0m };
        var quoteResponse = await client.PostAsJsonAsync("/api/cart/quote", cart);
        quoteResponse.EnsureSuccessStatusCode();
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        var creation = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(), quote.ReviewToken, cart));
        creation.EnsureSuccessStatusCode();
        var order = (await creation.Content.ReadFromJsonAsync<OrderResponse>())!;
        (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", 1))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("InPreparation", 2))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("Ready", 3))).EnsureSuccessStatusCode();
        return (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
    }
    private static string Path(Guid id) => $"/api/dispatch/orders/{id}/status";
    private static async Task<DispatchOrderResponse> MoveAsync(HttpClient client, Guid id, string status, int version)
    {
        var response = await client.PutAsJsonAsync(Path(id), new DispatchStatusRequest(status, version));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DispatchOrderResponse>())!;
    }
    private static async Task ProblemAsync(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.Conflict)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }
    private static async Task<PaymentResponse> PayAsync(HttpClient client, Guid id, int version)
    {
        var response = await client.PostAsJsonAsync($"/api/orders/{id}/payments", new CreatePaymentRequest(Guid.NewGuid(), "Pix", version));
        response.EnsureSuccessStatusCode();
        var payment = (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
        (await client.PutAsJsonAsync($"/api/orders/{id}/payments/{payment.Id}/receive", new ReceivePaymentRequest(1, true))).EnsureSuccessStatusCode();
        return payment;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DispatchRoutesDenyOtherRoles(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var expected = role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await client.GetAsync("/api/dispatch/orders")).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync(Path(Guid.NewGuid()), new DispatchStatusRequest("Delivered", 4))).StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FulfillmentFlowPreservesPurchaseAndRequiresReceivedPaymentToFinalize(bool delivery)
    {
        using var admin = await SignInAsync();
        using var dispatch = await SignInAsync(4);
        var order = await ReadyAsync(admin, delivery);
        var version = 4;
        if (delivery)
        {
            await MoveAsync(dispatch, order.Id, "AwaitingDelivery", version++);
            await MoveAsync(dispatch, order.Id, "OutForDelivery", version++);
        }
        var delivered = await MoveAsync(dispatch, order.Id, "Delivered", version++);
        Assert.Equal(order.Total, delivered.Total);
        Assert.Equal(order.Items[0].Name, delivered.Items[0].Name);
        await ProblemAsync(await dispatch.PutAsJsonAsync(Path(order.Id), new DispatchStatusRequest("Finalized", version)), "OrderPaymentRequired");
        var payment = await PayAsync(admin, order.Id, version);
        var done = await MoveAsync(dispatch, order.Id, "Finalized", version++);
        Assert.Equal(version, done.Version);
        Assert.Equal("Received", done.Payment!.Status);
        var saved = (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        Assert.Equal(version, saved.History.Length);
        Assert.Equal("Finalized", saved.History[^1].ToStatus);
        Assert.All(saved.History, entry => Assert.NotEqual(default, entry.OccurredAt));
        Assert.Empty((await dispatch.GetFromJsonAsync<DispatchPageResponse>("/api/dispatch/orders?status=Delivered"))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatch.GetAsync($"/api/orders/{order.Id}/payments")).StatusCode);
        await ProblemAsync(await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", version, "Desistiu")), "OrderTransitionDenied");
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/payments/{payment.Id}/refund", new RefundPaymentRequest(2, "Devolução após conclusão", true))).EnsureSuccessStatusCode();
        await ProblemAsync(await admin.PostAsJsonAsync($"/api/orders/{order.Id}/payments", new CreatePaymentRequest(Guid.NewGuid(), "Pix", version)), "PaymentOrderFinalized");
    }
    [Theory]
    [InlineData(false, "AwaitingDelivery")]
    [InlineData(false, "OutForDelivery")]
    [InlineData(false, "Finalized")]
    [InlineData(true, "Delivered")]
    [InlineData(true, "OutForDelivery")]
    [InlineData(true, "Finalized")]
    public async Task SkipsAndWrongFulfillmentAreRejected(bool delivery, string status)
    {
        using var admin = await SignInAsync();
        var order = await ReadyAsync(admin, delivery);
        await ProblemAsync(await admin.PutAsJsonAsync(Path(order.Id), new DispatchStatusRequest(status, 4)), "OrderTransitionDenied");
        Assert.Equal(4, (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.Version);
    }
    [Fact]
    public async Task QueuePaginatesClampsAndPreservesAddressSnapshot()
    {
        using var admin = await SignInAsync();
        var first = await ReadyAsync(admin, true);
        var second = await ReadyAsync(admin);
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Addresses.Where(address => address.Id == first.Address!.Id).ExecuteUpdateAsync(set => set.SetProperty(address => address.Street, "Alterada"));
            await database.Products.Where(p => p.Id == product.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.Name, "Alterado"));
        });
        var page = (await admin.GetFromJsonAsync<DispatchPageResponse>("/api/dispatch/orders?pageSize=1"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal("Rua histórica", page.Items[0].Address!.Street);
        Assert.Equal("Uai Sô", page.Items[0].Items[0].Name);
        Assert.Equal(second.Id, Assert.Single((await admin.GetFromJsonAsync<DispatchPageResponse>("/api/dispatch/orders?pageSize=1&page=999"))!.Items).Id);
        foreach (var query in new[] { "page=0", "pageSize=51", "status=Finalized", "status=New" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/dispatch/orders?" + query)).StatusCode);
    }
    [Fact]
    public async Task ConcurrentOperatorsAndReplayProduceOneHistoryEvent()
    {
        using var admin = await SignInAsync();
        using var first = await SignInAsync(4);
        using var second = await SignInAsync(4);
        var order = await ReadyAsync(admin);
        var results = await Task.WhenAll(first.PutAsJsonAsync(Path(order.Id), new DispatchStatusRequest("Delivered", 4)), second.PutAsJsonAsync(Path(order.Id), new DispatchStatusRequest("Delivered", 4)));
        Assert.Single(results, value => value.IsSuccessStatusCode);
        Assert.Single(results, value => value.StatusCode == HttpStatusCode.Conflict);
        var winner = results[0].IsSuccessStatusCode ? first : second;
        await MoveAsync(winner, order.Id, "Delivered", 4);
        Assert.Equal(5, (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History.Length);
    }
    [Fact]
    public async Task CancellationAndDispatchSerializeAndLeaveConsistentHistory()
    {
        using var admin = await SignInAsync();
        using var dispatch = await SignInAsync(4);
        var order = await ReadyAsync(admin, true);
        var results = await Task.WhenAll(dispatch.PutAsJsonAsync(Path(order.Id), new DispatchStatusRequest("AwaitingDelivery", 4)),
            admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", 4, "Desistiu")));
        Assert.Single(results, value => value.IsSuccessStatusCode);
        Assert.Single(results, value => value.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(5, (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History.Length);
    }
    [Theory]
    [InlineData("AwaitingDelivery", 5)]
    [InlineData("OutForDelivery", 6)]
    public async Task AdministratorCanCancelBeforeDeliveryOnlyAfterResolvingPayment(string stage, int version)
    {
        using var admin = await SignInAsync();
        using var attendant = await SignInAsync(2);
        var order = await ReadyAsync(admin, true);
        await MoveAsync(admin, order.Id, "AwaitingDelivery", 4);
        if (stage == "OutForDelivery")
        { await MoveAsync(admin, order.Id, stage, 5); }
        await ProblemAsync(await attendant.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", version, "Desistiu")), "OrderCancellationDenied", HttpStatusCode.Forbidden);
        var payment = await PayAsync(admin, order.Id, version);
        await ProblemAsync(await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", version, "Desistiu")), "OrderPaymentUnresolved");
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/payments/{payment.Id}/refund", new RefundPaymentRequest(2, "Devolvido", true))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", version, "Desistiu"))).EnsureSuccessStatusCode();
        Assert.Empty((await admin.GetFromJsonAsync<DispatchPageResponse>("/api/dispatch/orders?status=" + stage))!.Items);
    }
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, false)]
    [InlineData(4, false, true)]
    public async Task PrintPermissionsAndProductionProjection(int role, bool kitchen, bool dispatch)
    {
        using var admin = await SignInAsync();
        var order = await ReadyAsync(admin, true);
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        foreach (var (mode, allowed) in new[] { ("kitchen", kitchen), ("dispatch", dispatch) })
        {
            var response = await client.GetAsync($"/api/print/orders/{order.Id}/{mode}");
            Assert.Equal(allowed ? HttpStatusCode.OK : role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
            if (!allowed)
            { continue; }
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var printed = json.RootElement.GetProperty("order");
            Assert.Equal(order.Number, printed.GetProperty("number").GetInt32());
            if (mode == "kitchen")
            {
                Assert.False(printed.TryGetProperty("customerName", out _));
                Assert.False(printed.TryGetProperty("address", out _));
                Assert.False(printed.TryGetProperty("total", out _));
                Assert.False(printed.TryGetProperty("payment", out _));
                Assert.False(printed.GetProperty("items")[0].TryGetProperty("unitPrice", out _));
            }
            else
            { Assert.Equal("Rua histórica", printed.GetProperty("address").GetProperty("street").GetString()); }
        }
        Assert.Equal(4, (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.Version);
    }
    [Fact]
    public async Task PrintShowsCurrentCancelledStatusAndDoesNotChangeOrder()
    {
        using var admin = await SignInAsync();
        var order = await ReadyAsync(admin);
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", 4, "Desistiu"))).EnsureSuccessStatusCode();
        foreach (var mode in new[] { "kitchen", "dispatch" })
        {
            using var json = JsonDocument.Parse(await admin.GetStringAsync($"/api/print/orders/{order.Id}/{mode}"));
            Assert.Equal("Cancelled", json.RootElement.GetProperty("order").GetProperty("status").GetString());
            await ProblemAsync(await admin.GetAsync($"/api/print/orders/{Guid.NewGuid()}/{mode}"), "OrderNotFound", HttpStatusCode.NotFound);
        }
    }
    [Fact]
    public async Task InvalidVersionMissingOrderAndForgedFieldsAreRejected()
    {
        using var admin = await SignInAsync();
        var order = await ReadyAsync(admin);
        await ProblemAsync(await admin.PutAsJsonAsync(Path(order.Id), new DispatchStatusRequest("Delivered", 3)), "OrderVersionConflict");
        await ProblemAsync(await admin.PutAsJsonAsync(Path(Guid.NewGuid()), new DispatchStatusRequest("Delivered", 4)), "OrderNotFound", HttpStatusCode.NotFound);
        foreach (var input in new[] { new DispatchStatusRequest("Cancelled", 4), new("Delivered", null), new("Delivered", 0) })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Path(order.Id), input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Path(order.Id), new { status = "Delivered", expectedVersion = 4, total = 0 })).StatusCode);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServiceRechecksDispatchSessionAndPermission(bool revoke)
    {
        using var admin = await SignInAsync();
        var order = await ReadyAsync(admin);
        var actor = await factory.CreateUserAsync(4);
        await factory.WithDatabaseAsync(async database =>
        {
            if (revoke)
            { await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.SecurityStamp, Guid.NewGuid())); }
            else
            { await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.RoleId, 2)); }
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<OrderException>(() => scope.ServiceProvider.GetRequiredService<DispatchService>()
            .SetStatusAsync(actor.Id, actor.SecurityStamp, order.Id, new DispatchStatusRequest("Delivered", 4), CancellationToken.None));
        Assert.Equal(revoke ? OrderError.InvalidSession : OrderError.PermissionDenied, error.Error);
    }
}
