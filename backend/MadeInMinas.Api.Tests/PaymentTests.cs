using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.DTOs.Payments;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class PaymentTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Customer customer = new() { Name = "Maria", NormalizedName = "MARIA", Phone = "+5531999991234" };
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

    private async Task<OrderResponse> OrderAsync(HttpClient client)
    {
        var cart = new CartQuoteRequest(customer.Id, "Pickup", [new(product.Id, 2, null)], null, null) { DeliveryFee = 0 };
        var review = await client.PostAsJsonAsync("/api/cart/quote", cart);
        review.EnsureSuccessStatusCode();
        var quote = (await review.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(), quote.ReviewToken, cart));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static string Path(Guid orderId) => $"/api/orders/{orderId}/payments";
    private static async Task<PaymentResponse> CreateAsync(HttpClient client, OrderResponse order, string method = "Pix")
    {
        var response = await client.PostAsJsonAsync(Path(order.Id), new CreatePaymentRequest(Guid.NewGuid(), method, order.Version));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
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
    public async Task OnlySalesStaffCanAccessPayments(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var expected = role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var path = Path(Guid.NewGuid());
        Assert.Equal(expected, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(expected, (await client.GetAsync(path + "/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        foreach (var action in new[] { "receive", "cancel", "refund" })
            Assert.Equal(expected, (await client.PutAsJsonAsync(path + "/" + Guid.NewGuid() + "/" + action, new { })).StatusCode);
    }

    [Theory]
    [InlineData("Cash", 1)]
    [InlineData("Pix", 2)]
    [InlineData("CreditCard", 1)]
    [InlineData("DebitCard", 2)]
    public async Task ReceiptUsesSavedOrderAmountAndPreservesProductionStatus(string method, int role)
    {
        using var client = await SignInAsync(role);
        var order = await OrderAsync(client);
        await factory.WithDatabaseAsync(async database => await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 99m)));
        var payment = await CreateAsync(client, order, method);
        Assert.Equal(59.80m, payment.Amount);
        Assert.Equal("Pending", payment.Status);
        Assert.Single(payment.History);
        var request = new ReceivePaymentRequest(1, true, method == "Cash" ? 100m : null);
        var path = Path(order.Id) + $"/{payment.Id}/receive";
        var receipts = await Task.WhenAll(client.PutAsJsonAsync(path, request), client.PutAsJsonAsync(path, request));
        Assert.All(receipts, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var received = (await receipts[0].Content.ReadFromJsonAsync<PaymentResponse>())!;
        Assert.Equal("Received", received.Status);
        Assert.Equal(2, received.Version);
        Assert.Equal(method == "Cash" ? 40.20m : (decimal?)null, received.ChangeAmount);
        Assert.Equal(new[] { "Pending", "Received" }, received.History.Select(entry => entry.ToStatus));
        Assert.All(received.History, entry => Assert.False(string.IsNullOrWhiteSpace(entry.ActorName)));
        var page = (await client.GetFromJsonAsync<PaymentPageResponse>(Path(order.Id)))!;
        Assert.Equal(order.Total, page.ReceivedAmount);
        Assert.Equal(0, page.Balance);
        Assert.Equal("New", page.OrderStatus);
        Assert.Equal(payment.Id, page.ActivePayment!.Id);
        Assert.Equal(2, (await client.GetFromJsonAsync<PaymentResponse>(Path(order.Id) + "/" + payment.Id))!.History.Length);
    }

    [Fact]
    public async Task ConcurrentCreationAndReplaysCannotDuplicatePayment()
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        var request = new CreatePaymentRequest(Guid.NewGuid(), "Pix", 1);
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.PostAsJsonAsync(Path(order.Id), request)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var payment = (await responses[0].Content.ReadFromJsonAsync<PaymentResponse>())!;
        await ProblemAsync(await client.PostAsJsonAsync(Path(order.Id), request with { Method = "Cash" }), "PaymentRequestConflict");
        await ProblemAsync(await client.PostAsJsonAsync(Path(order.Id), request with { RequestId = Guid.NewGuid() }), "PaymentAlreadyActive");
        (await client.PutAsJsonAsync(Path(order.Id) + $"/{payment.Id}/cancel", new CancelPaymentRequest(1, "Troca de forma"))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", 1, "Desistiu"))).EnsureSuccessStatusCode();
        var replay = await client.PostAsJsonAsync(Path(order.Id), request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("Cancelled", (await replay.Content.ReadFromJsonAsync<PaymentResponse>())!.Status);
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Payments.CountAsync()));
    }

    [Fact]
    public async Task DifferentConcurrentAttemptsAllowOnlyOneActivePayment()
    {
        using var admin = await SignInAsync();
        using var attendant = await SignInAsync(2);
        var order = await OrderAsync(admin);
        var responses = await Task.WhenAll(admin.PostAsJsonAsync(Path(order.Id), new CreatePaymentRequest(Guid.NewGuid(), "Cash", 1)),
            attendant.PostAsJsonAsync(Path(order.Id), new CreatePaymentRequest(Guid.NewGuid(), "Pix", 1)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        await ProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), "PaymentAlreadyActive");
    }

    [Fact]
    public async Task OrderCancellationRequiresResolvingPaymentAndRefundRequiresAdministrator()
    {
        using var attendant = await SignInAsync(2);
        using var admin = await SignInAsync();
        var order = await OrderAsync(attendant);
        var payment = await CreateAsync(attendant, order);
        var path = Path(order.Id) + "/" + payment.Id;
        var cancelOrder = new OrderStatusRequest("Cancelled", 1, "Desistiu");
        await ProblemAsync(await attendant.PutAsJsonAsync($"/api/orders/{order.Id}/status", cancelOrder), "OrderPaymentUnresolved");
        (await attendant.PutAsJsonAsync(path + "/receive", new ReceivePaymentRequest(1, true))).EnsureSuccessStatusCode();
        await ProblemAsync(await attendant.PutAsJsonAsync($"/api/orders/{order.Id}/status", cancelOrder), "OrderPaymentUnresolved");
        Assert.Equal(HttpStatusCode.Forbidden, (await attendant.PutAsJsonAsync(path + "/refund", new RefundPaymentRequest(2, "Devolvido via Pix", true))).StatusCode);
        await ProblemAsync(await admin.PutAsJsonAsync(path + "/cancel", new CancelPaymentRequest(2, "Desistiu")), "PaymentTransitionDenied");
        var refund = new RefundPaymentRequest(2, " Devolvido via Pix ", true);
        var response = await admin.PutAsJsonAsync(path + "/refund", refund);
        response.EnsureSuccessStatusCode();
        var refunded = (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
        Assert.Equal("Refunded", refunded.Status);
        Assert.Equal("Devolvido via Pix", refunded.History[2].Reason);
        Assert.NotEqual(refunded.History[1].ActorId, refunded.History[2].ActorId);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path + "/refund", refund)).StatusCode);
        var summary = (await admin.GetFromJsonAsync<PaymentPageResponse>(Path(order.Id)))!;
        Assert.Equal(0, summary.ReceivedAmount);
        Assert.Equal(order.Total, summary.Balance);
        Assert.Null(summary.ActivePayment);
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", cancelOrder)).EnsureSuccessStatusCode();
        await ProblemAsync(await admin.PostAsJsonAsync(Path(order.Id), new CreatePaymentRequest(Guid.NewGuid(), "Pix", 2)), "PaymentOrderCancelled");
        Assert.Equal(0, (await admin.GetFromJsonAsync<PaymentPageResponse>(Path(order.Id)))!.Balance);
    }

    [Fact]
    public async Task CancelledAttemptsRemainInPagedHistoryAndActivePaymentIsAlwaysReturned()
    {
        using var client = await SignInAsync(2);
        var order = await OrderAsync(client);
        var first = await CreateAsync(client, order);
        var cancel = new CancelPaymentRequest(1, "Troca para dinheiro");
        var path = Path(order.Id) + $"/{first.Id}/cancel";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path, cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path, cancel)).StatusCode);
        await ProblemAsync(await client.PutAsJsonAsync(path, cancel with { Reason = "Outro" }), "PaymentVersionConflict");
        var second = await CreateAsync(client, order, "Cash");
        var page = (await client.GetFromJsonAsync<PaymentPageResponse>(Path(order.Id) + "?page=2&pageSize=1"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal(2, page.Items[0].History.Length);
        Assert.Equal(second.Id, page.ActivePayment!.Id);
        Assert.Equal(order.Total, page.Balance);
        foreach (var query in new[] { "page=0", "pageSize=101" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Path(order.Id) + "?" + query)).StatusCode);
    }

    [Fact]
    public async Task VersionsAndOrderOwnershipAreChecked()
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        var other = await OrderAsync(client);
        (await client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", 1))).EnsureSuccessStatusCode();
        await ProblemAsync(await client.PostAsJsonAsync(Path(order.Id), new CreatePaymentRequest(Guid.NewGuid(), "Pix", 1)), "PaymentOrderChanged");
        var payment = await CreateAsync(client, order with { Version = 2 });
        await ProblemAsync(await client.PutAsJsonAsync(Path(order.Id) + $"/{payment.Id}/receive", new ReceivePaymentRequest(2, true)), "PaymentVersionConflict");
        await ProblemAsync(await client.GetAsync(Path(other.Id) + "/" + payment.Id), "PaymentNotFound", HttpStatusCode.NotFound);
        await ProblemAsync(await client.PutAsJsonAsync(Path(other.Id) + $"/{payment.Id}/receive", new ReceivePaymentRequest(1, true)), "PaymentNotFound", HttpStatusCode.NotFound);
        await ProblemAsync(await client.GetAsync(Path(Guid.NewGuid())), "PaymentOrderNotFound", HttpStatusCode.NotFound);
        await ProblemAsync(await client.PostAsJsonAsync(Path(Guid.NewGuid()), new CreatePaymentRequest(Guid.NewGuid(), "Pix", 1)), "PaymentOrderNotFound", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidMoneyAcknowledgementsReasonsAndInjectedAmountsAreRejected()
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        foreach (var request in new[] { new CreatePaymentRequest(null, "Cash", 1), new(Guid.Empty, "Pix", 1), new(Guid.NewGuid(), "Other", 1), new(Guid.NewGuid(), "Cash", 0) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(order.Id), request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(order.Id), new { requestId = Guid.NewGuid(), method = "Pix", expectedOrderVersion = 1, amount = 1 })).StatusCode);
        var payment = await CreateAsync(client, order, "Cash");
        var path = Path(order.Id) + "/" + payment.Id;
        foreach (var input in new[] { new ReceivePaymentRequest(1, false, 100), new(1, null, 100), new(null, true, 100), new(1, true, 100.001m), new(1, true, -1), new(1, true, 10000000000m) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/receive", input)).StatusCode);
        foreach (var cash in new decimal?[] { null, 59.79m })
            await ProblemAsync(await client.PutAsJsonAsync(path + "/receive", new ReceivePaymentRequest(1, true, cash)), "PaymentInvalidCash", HttpStatusCode.BadRequest);
        foreach (var reason in new[] { "", " ", new string('x', 501) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/cancel", new CancelPaymentRequest(1, reason))).StatusCode);
        (await client.PutAsJsonAsync(path + "/receive", new ReceivePaymentRequest(1, true, 59.80m))).EnsureSuccessStatusCode();
        foreach (var input in new[] { new RefundPaymentRequest(2, "Devolvido", false), new(2, "Devolvido", null), new(2, " ", true) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/refund", input)).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<PaymentResponse>(path))!.History.Length);
    }

    [Theory]
    [InlineData("Pix")]
    [InlineData("CreditCard")]
    [InlineData("DebitCard")]
    public async Task NonCashPaymentsCannotRecordCashTendered(string method)
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        var payment = await CreateAsync(client, order, method);
        await ProblemAsync(await client.PutAsJsonAsync(Path(order.Id) + $"/{payment.Id}/receive", new ReceivePaymentRequest(1, true, 100)), "PaymentInvalidCash", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConcurrentOrderCancellationAndPaymentCreationCannotLeaveCancelledOrderWithActivePayment()
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(Path(order.Id), new CreatePaymentRequest(Guid.NewGuid(), "Pix", 1)),
            client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", 1, "Desistiu")));
        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var summary = (await client.GetFromJsonAsync<PaymentPageResponse>(Path(order.Id)))!;
        Assert.False(summary.OrderStatus == "Cancelled" && summary.ActivePayment is not null);
    }

    [Fact]
    public async Task ConcurrentReceiptAndCancellationRecordOnlyOneDecision()
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        var payment = await CreateAsync(client, order);
        var path = Path(order.Id) + "/" + payment.Id;
        var responses = await Task.WhenAll(client.PutAsJsonAsync(path + "/receive", new ReceivePaymentRequest(1, true)),
            client.PutAsJsonAsync(path + "/cancel", new CancelPaymentRequest(1, "Desistiu")));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        await ProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), "PaymentVersionConflict");
        Assert.Equal(2, (await client.GetFromJsonAsync<PaymentResponse>(path))!.History.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServiceRechecksSessionAndCurrentRole(bool revokeSession)
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        var actor = await factory.CreateUserAsync(2);
        await factory.WithDatabaseAsync(async database =>
        {
            if (revokeSession)
                await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.SecurityStamp, Guid.NewGuid()));
            else
                await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.RoleId, 3));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<PaymentService>();
        var error = await Assert.ThrowsAsync<PaymentException>(() => service.CreateAsync(actor.Id, actor.SecurityStamp, order.Id, new CreatePaymentRequest(Guid.NewGuid(), "Pix", 1), CancellationToken.None));
        Assert.Equal(revokeSession ? PaymentError.InvalidSession : PaymentError.PermissionDenied, error.Error);
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateActivePaymentAndInvalidCash()
    {
        using var client = await SignInAsync();
        var order = await OrderAsync(client);
        var payment = await CreateAsync(client, order);
        await factory.WithDatabaseAsync(async database =>
        {
            var saved = await database.Payments.AsNoTracking().SingleAsync();
            saved.Id = Guid.NewGuid();
            saved.RequestId = Guid.NewGuid();
            database.Payments.Add(saved);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        });
        await factory.WithDatabaseAsync(async database =>
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => database.Payments.Where(value => value.Id == payment.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(value => value.CashTendered, 100m)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        });
    }
}
