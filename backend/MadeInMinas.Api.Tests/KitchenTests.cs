using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Kitchen;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.DTOs.Payments;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class KitchenTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Customer customer = new() { Name = "Cliente reservado", NormalizedName = "CLIENTE RESERVADO", Phone = "+5531999991234" };
    private Product product = null!;
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
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
        database.Customers.Add(customer);
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => factory.WithDatabaseAsync(async database =>
    {
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
    private async Task<OrderResponse> CreateAsync(HttpClient client, bool confirmed = true)
    {
        var cart = new CartQuoteRequest(customer.Id, "Pickup", [new(product.Id, 2, "Sem cebola")], null, "Embalagem separada");
        var quoteResponse = await client.PostAsJsonAsync("/api/cart/quote", cart);
        quoteResponse.EnsureSuccessStatusCode();
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(), quote.ReviewToken, cart));
        response.EnsureSuccessStatusCode();
        var order = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        if (!confirmed)
            return order;
        var confirmation = await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", 1));
        confirmation.EnsureSuccessStatusCode();
        return (await confirmation.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
    private static string Path(Guid id) => $"/api/kitchen/orders/{id}/status";
    private static async Task ProblemAsync(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.Conflict)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task OnlyKitchenAndAdministratorCanAccess(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var status = role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(status, (await client.GetAsync("/api/kitchen/orders")).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(Path(Guid.NewGuid()), new KitchenStatusRequest("Ready", 1))).StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ProductionFlowPreservesPurchaseAndRecordsDatesAndActor(int role)
    {
        using var sales = await SignInAsync();
        using var kitchen = await SignInAsync(role);
        var order = await CreateAsync(sales);
        await factory.WithDatabaseAsync(async database => await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Name, "Novo nome").SetProperty(item => item.IsAvailable, false)));
        foreach (var (status, version) in new[] { ("InPreparation", 2), ("Ready", 3) })
        {
            var response = await kitchen.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest(status, version));
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var card = (await response.Content.ReadFromJsonAsync<KitchenOrderResponse>())!;
            Assert.Equal(status, card.Status);
            Assert.Equal(version + 1, card.Version);
            Assert.Equal("Uai Sô", Assert.Single(card.Items).Name);
            Assert.Equal("Sem cebola", card.Items[0].Notes);
            Assert.Equal(2, card.Items[0].Quantity);
            Assert.Equal("Embalagem separada", card.Notes);
            Assert.NotNull(card.ConfirmedAt);
            Assert.NotNull(card.PreparationStartedAt);
            if (status == "Ready")
                Assert.NotNull(card.ReadyAt);
        }
        var saved = (await sales.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        Assert.Equal(order.Total, saved.Total);
        Assert.Equal(new[] { "New", "Confirmed", "InPreparation", "Ready" }, saved.History.Select(value => value.ToStatus));
        Assert.NotEqual(saved.History[1].ActorId, saved.History[2].ActorId);
        Assert.Equal(saved.History[2].ActorId, saved.History[3].ActorId);
        var board = (await kitchen.GetFromJsonAsync<KitchenBoardResponse>("/api/kitchen/orders"))!;
        var ready = Assert.Single(board.Columns[2].Items);
        Assert.Equal(saved.History[1].OccurredAt, ready.ConfirmedAt);
        Assert.Equal(saved.History[2].OccurredAt, ready.PreparationStartedAt);
        Assert.Equal(saved.History[3].OccurredAt, ready.ReadyAt);
        Assert.True(board.ServerTime >= ready.ReadyAt);
        Assert.Equal(order.Id, Assert.Single((await sales.GetFromJsonAsync<OrderPageResponse>("/api/orders?status=Ready"))!.Items).Id);
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.Payments.ToArrayAsync()));
    }

    [Fact]
    public async Task BoardIncludesOnlyProductionAndNeverExposesCustomerOrMoney()
    {
        using var sales = await SignInAsync();
        using var kitchen = await SignInAsync(3);
        await CreateAsync(sales, false);
        var cancelled = await CreateAsync(sales);
        (await sales.PutAsJsonAsync($"/api/orders/{cancelled.Id}/status", new OrderStatusRequest("Cancelled", 2, "Desistiu"))).EnsureSuccessStatusCode();
        var confirmed = await CreateAsync(sales);
        var response = await kitchen.GetAsync("/api/kitchen/orders");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(customer.Name, body);
        Assert.DoesNotContain(customer.Phone, body);
        using var json = JsonDocument.Parse(body);
        var card = json.RootElement.GetProperty("columns")[0].GetProperty("items")[0];
        Assert.Equal(new[] { "confirmedAt", "createdAt", "fulfillment", "id", "items", "notes", "number", "preparationStartedAt", "readyAt", "status", "version" }, card.EnumerateObject().Select(value => value.Name).Order());
        Assert.Equal(new[] { "name", "notes", "position", "quantity" }, card.GetProperty("items")[0].EnumerateObject().Select(value => value.Name).Order());
        var board = (await response.Content.ReadFromJsonAsync<KitchenBoardResponse>())!;
        Assert.Equal(confirmed.Id, Assert.Single(board.Columns[0].Items).Id);
        Assert.Empty(board.Columns[1].Items);
        Assert.Empty(board.Columns[2].Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync($"/api/orders/{confirmed.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync($"/api/orders/{confirmed.Id}/payments")).StatusCode);
    }

    [Fact]
    public async Task BoardPaginatesEachColumnAndClampsEmptyLastPage()
    {
        using var client = await SignInAsync();
        var first = await CreateAsync(client, false);
        var second = await CreateAsync(client);
        (await client.PutAsJsonAsync($"/api/orders/{first.Id}/status", new OrderStatusRequest("Confirmed", 1))).EnsureSuccessStatusCode();
        var board = (await client.GetFromJsonAsync<KitchenBoardResponse>("/api/kitchen/orders?pageSize=1"))!;
        Assert.Equal(second.Id, Assert.Single(board.Columns[0].Items).Id);
        Assert.Equal(2, board.Columns[0].TotalCount);
        var next = (await client.GetFromJsonAsync<KitchenBoardResponse>("/api/kitchen/orders?pageSize=1&confirmedPage=2&readyPage=100"))!;
        Assert.Equal(first.Id, Assert.Single(next.Columns[0].Items).Id);
        Assert.Equal(1, next.Columns[2].Page);
        (await client.PutAsJsonAsync(Path(first.Id), new KitchenStatusRequest("InPreparation", 2))).EnsureSuccessStatusCode();
        var clamped = (await client.GetFromJsonAsync<KitchenBoardResponse>("/api/kitchen/orders?pageSize=1&confirmedPage=2"))!;
        Assert.Equal(1, clamped.Columns[0].Page);
        Assert.Equal(second.Id, Assert.Single(clamped.Columns[0].Items).Id);
        Assert.Equal(first.Id, Assert.Single(clamped.Columns[1].Items).Id);
        foreach (var query in new[] { "confirmedPage=0", "preparingPage=-1", "readyPage=1000001", "pageSize=51" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/kitchen/orders?" + query)).StatusCode);
    }

    [Fact]
    public async Task SkipsReversalsAndWrongVersionsCannotChangeProduction()
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client, false);
        await ProblemAsync(await client.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("InPreparation", 1)), "OrderTransitionDenied");
        (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", 1))).EnsureSuccessStatusCode();
        await ProblemAsync(await client.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("Ready", 2)), "OrderTransitionDenied");
        await ProblemAsync(await client.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("InPreparation", 1)), "OrderVersionConflict");
        (await client.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("InPreparation", 2))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("Ready", 3))).EnsureSuccessStatusCode();
        await ProblemAsync(await client.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("InPreparation", 4)), "OrderTransitionDenied");
        await ProblemAsync(await client.PutAsJsonAsync(Path(Guid.NewGuid()), new KitchenStatusRequest("Ready", 3)), "OrderNotFound", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReplaysAndCompetingOperatorsDoNotDuplicateHistory()
    {
        using var admin = await SignInAsync();
        using var kitchen = await SignInAsync(3);
        var order = await CreateAsync(admin);
        var input = new KitchenStatusRequest("InPreparation", 2);
        var concurrent = await Task.WhenAll(admin.PutAsJsonAsync(Path(order.Id), input), kitchen.PutAsJsonAsync(Path(order.Id), input));
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.OK);
        await ProblemAsync(Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Conflict), "OrderVersionConflict");
        var client = concurrent[0].IsSuccessStatusCode ? admin : kitchen;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Path(order.Id), input)).StatusCode);
        var ready = new KitchenStatusRequest("Ready", 3);
        var replays = await Task.WhenAll(client.PutAsJsonAsync(Path(order.Id), ready), client.PutAsJsonAsync(Path(order.Id), ready));
        Assert.All(replays, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(4, (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History.Length);
    }

    [Theory]
    [InlineData("Confirmed", 2)]
    [InlineData("InPreparation", 3)]
    [InlineData("Ready", 4)]
    public async Task AdministrativeCancellationRespectsPaymentsAndRemovesOrderFromBoard(string stage, int version)
    {
        using var admin = await SignInAsync();
        using var attendant = await SignInAsync(2);
        using var kitchen = await SignInAsync(3);
        var order = await CreateAsync(admin);
        var payment = await admin.PostAsJsonAsync($"/api/orders/{order.Id}/payments", new CreatePaymentRequest(Guid.NewGuid(), "Pix", 2));
        payment.EnsureSuccessStatusCode();
        var pending = (await payment.Content.ReadFromJsonAsync<PaymentResponse>())!;
        if (stage != "Confirmed")
            (await kitchen.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("InPreparation", 2))).EnsureSuccessStatusCode();
        if (stage == "Ready")
            (await kitchen.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("Ready", 3))).EnsureSuccessStatusCode();
        var cancel = new OrderStatusRequest("Cancelled", version, "Cliente desistiu");
        await ProblemAsync(await attendant.PutAsJsonAsync($"/api/orders/{order.Id}/status", cancel), "OrderCancellationDenied", HttpStatusCode.Forbidden);
        await ProblemAsync(await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", cancel), "OrderPaymentUnresolved");
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/payments/{pending.Id}/cancel", new CancelPaymentRequest(1, "Desistiu"))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", cancel)).EnsureSuccessStatusCode();
        var board = (await kitchen.GetFromJsonAsync<KitchenBoardResponse>("/api/kitchen/orders"))!;
        Assert.All(board.Columns, column => Assert.Empty(column.Items));
        await ProblemAsync(await kitchen.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("Ready", version)), "OrderVersionConflict");
    }

    [Fact]
    public async Task ProductionAndCancellationAreSerializedOnSameOrder()
    {
        using var admin = await SignInAsync();
        using var kitchen = await SignInAsync(3);
        var order = await CreateAsync(admin);
        var results = await Task.WhenAll(kitchen.PutAsJsonAsync(Path(order.Id), new KitchenStatusRequest("InPreparation", 2)),
            admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", 2, "Desistiu")));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        await ProblemAsync(Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict), "OrderVersionConflict");
        Assert.Equal(3, (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History.Length);
    }

    [Fact]
    public async Task InvalidCommandsAndCommercialFieldsAreRejected()
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client);
        foreach (var input in new[] { new KitchenStatusRequest("Confirmed", 2), new("Cancelled", 2), new("New", 2), new("Ready", null), new("InPreparation", 0) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Path(order.Id), input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Path(order.Id), new { status = "InPreparation", expectedVersion = 2, total = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "InPreparation", expectedVersion = 2 })).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.History.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServiceRechecksCurrentSessionAndKitchenPermission(bool revoke)
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client);
        var actor = await factory.CreateUserAsync(3);
        await factory.WithDatabaseAsync(async database =>
        {
            if (revoke)
                await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.SecurityStamp, Guid.NewGuid()));
            else
                await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.RoleId, 2));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<KitchenService>();
        var error = await Assert.ThrowsAsync<OrderException>(() => service.SetStatusAsync(actor.Id, actor.SecurityStamp, order.Id, new KitchenStatusRequest("InPreparation", 2), CancellationToken.None));
        Assert.Equal(revoke ? OrderError.InvalidSession : OrderError.PermissionDenied, error.Error);
    }

    [Fact]
    public async Task DatabaseRejectsUnsupportedStatusAndSkippedProductionHistory()
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.Orders.Where(value => value.Id == order.Id).ExecuteUpdateAsync(set => set.SetProperty(value => value.Status, "Delivered")));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        });
        await factory.WithDatabaseAsync(async database =>
        {
            var actor = order.History[1];
            database.OrderStatusHistory.Add(new OrderStatusHistory { OrderId = order.Id, Version = 3, FromStatus = "Confirmed", ToStatus = "Ready", ActorId = actor.ActorId, ActorName = actor.ActorName, OccurredAt = DateTimeOffset.UtcNow });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        });
    }
}
