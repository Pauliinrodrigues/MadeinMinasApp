using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.DTOs.Payments;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.DTOs.PublicOrders;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class PublicOrderTrackingTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private const string Path = "/api/public-orders/tracking";
    private Product burger = null!;

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
        burger = new Product
        {
            Name = "Uai Sô",
            NormalizedName = "UAI SÔ",
            Price = 29.90m,
            IsActive = true,
            IsAvailable = true,
            Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" }
        };
        database.Products.Add(burger);
        OrderStockFixture.AddRecipe(database, burger);
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    private async Task<(PublicOrderReceipt Receipt, PublicOrderRequest Request)> CreateAsync(HttpClient client)
    {
        var checkout = new PublicCheckoutRequest("Contato privado", "31999991234", new([new(burger.Id, 1, "Nota privada do item")], "Nota privada do pedido"));
        var review = await client.PostAsJsonAsync("/api/public-checkout/review", checkout);
        review.EnsureSuccessStatusCode();
        var input = new PublicOrderRequest(Guid.NewGuid(), (await review.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!.ReviewToken, checkout);
        var response = await client.PostAsJsonAsync("/api/public-checkout/orders", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Assert.NotNull(receipt.Tracking);
        return (receipt, input);
    }

    private static async Task<HttpResponseMessage> ReadAsync(HttpClient client, string? token, string path = Path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
            request.Headers.Add("X-Order-Access", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task CredentialReadsOnlyItsOrderAndDoesNotGrantStaffAccess()
    {
        using var client = factory.CreateStaffClient();
        var (first, input) = await CreateAsync(client);
        var (second, _) = await CreateAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        var response = await ReadAsync(client, first.Tracking!.Token, Path + $"?number={second.Number}&phone=31999991234");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.Equal(["createdAt", "fulfillment", "history", "number", "status", "total", "updatedAt"],
            json.RootElement.EnumerateObject().Select(field => field.Name).Order());
        Assert.Equal(["occurredAt", "status"], json.RootElement.GetProperty("history")[0].EnumerateObject().Select(field => field.Name).Order());
        var order = JsonSerializer.Deserialize<PublicOrderTrackingResponse>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(first.Number, order.Number);
        Assert.Equal("New", order.Status);
        Assert.Equal(first.CreatedAt, order.History.Single().OccurredAt);
        Assert.DoesNotContain("privad", text);
        Assert.DoesNotContain("31999991234", text);
        Assert.DoesNotContain(input.RequestId.ToString()!, text);
        var replay = await client.PostAsJsonAsync("/api/public-checkout/orders", input);
        var recovered = (await replay.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Assert.Equal(first.Tracking.ExpiresAt, recovered.Tracking!.ExpiresAt);
        var recoveredResponse = await ReadAsync(client, recovered.Tracking.Token);
        Assert.Equal(first.Number, (await recoveredResponse.Content.ReadFromJsonAsync<PublicOrderTrackingResponse>())!.Number);
        var otherResponse = await ReadAsync(client, second.Tracking!.Token);
        Assert.Equal(second.Number, (await otherResponse.Content.ReadFromJsonAsync<PublicOrderTrackingResponse>())!.Number);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Tracking.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers")).StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(2, await database.Orders.CountAsync());
            Assert.Equal(2, await database.OrderStatusHistory.CountAsync());
            Assert.False(await database.Payments.AnyAsync());
            Assert.False(await database.StockMovements.AnyAsync());
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("number")]
    [InlineData("phone")]
    [InlineData("requestId")]
    [InlineData("reviewToken")]
    [InlineData("tampered")]
    [InlineData("oversized")]
    [InlineData("query")]
    [InlineData("unknownOrder")]
    [InlineData("expired")]
    public async Task InvalidAccessReturnsTheSameGenericNotFound(string condition)
    {
        using var client = factory.CreateStaffClient();
        var (receipt, input) = await CreateAsync(client);
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("MadeInMinas.PublicOrderTracking.v1");
        var id = Guid.Empty;
        await factory.WithDatabaseAsync(async database => id = (await database.Orders.SingleAsync()).Id);
        var token = condition switch
        {
            "missing" or "query" => null,
            "number" => receipt.Number.ToString(),
            "phone" => "+5531999991234",
            "requestId" => input.RequestId.ToString(),
            "reviewToken" => input.ReviewToken,
            "tampered" => receipt.Tracking!.Token.Insert(30, "x"),
            "oversized" => new string('a', 2049),
            "unknownOrder" => factory.Services.GetRequiredService<PublicOrderAccess>().Issue(Guid.NewGuid(), DateTimeOffset.UtcNow)!.Token,
            "expired" => protector.Protect(JsonSerializer.Serialize(new { OrderId = id, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) })),
            _ => throw new InvalidOperationException()
        };
        var response = await ReadAsync(client, token, condition == "query" ? Path + "?token=" + receipt.Tracking!.Token : Path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Acompanhamento indisponível.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("O acesso não está disponível ou expirou. Procure o atendimento.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ManualOrdersCannotBeReadEvenWithAServerIssuedCredential()
    {
        using var client = factory.CreateStaffClient();
        var (receipt, _) = await CreateAsync(client);
        var user = await factory.CreateUserAsync();
        await factory.WithDatabaseAsync(async database =>
            await database.Orders.ExecuteUpdateAsync(update => update.SetProperty(order => order.Origin, "Manual").SetProperty(order => order.CreatedById, user.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await ReadAsync(client, receipt.Tracking!.Token)).StatusCode);
    }

    [Fact]
    public async Task TrackingReflectsRealStaffKitchenAndDispatchTransitions()
    {
        using var guest = factory.CreateStaffClient();
        var (receipt, _) = await CreateAsync(guest);
        var id = Guid.Empty;
        await factory.WithDatabaseAsync(async database => id = (await database.Orders.SingleAsync()).Id);
        var user = await factory.CreateUserAsync();
        using var staff = factory.CreateStaffClient();
        var login = await staff.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        (await staff.PutAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Confirmed", 1))).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/kitchen/orders/{id}/status", new { status = "InPreparation", expectedVersion = 2 })).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/kitchen/orders/{id}/status", new { status = "Ready", expectedVersion = 3 })).EnsureSuccessStatusCode();
        var ready = await ReadAsync(guest, receipt.Tracking!.Token);
        var order = (await ready.Content.ReadFromJsonAsync<PublicOrderTrackingResponse>())!;
        Assert.Equal("Ready", order.Status);
        Assert.Equal(["New", "Confirmed", "InPreparation", "Ready"], order.History.Select(entry => entry.Status));
        Assert.Equal(order.UpdatedAt, order.History.Last().OccurredAt);
        var paymentResponse = await staff.PostAsJsonAsync($"/api/orders/{id}/payments", new CreatePaymentRequest(Guid.NewGuid(), "Cash", 4));
        paymentResponse.EnsureSuccessStatusCode();
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;
        (await staff.PutAsJsonAsync($"/api/orders/{id}/payments/{payment.Id}/receive", new ReceivePaymentRequest(1, true, receipt.Total))).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/dispatch/orders/{id}/status", new { status = "Delivered", expectedVersion = 4 })).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/dispatch/orders/{id}/status", new { status = "Finalized", expectedVersion = 5 })).EnsureSuccessStatusCode();
        var done = (await (await ReadAsync(guest, receipt.Tracking.Token)).Content.ReadFromJsonAsync<PublicOrderTrackingResponse>())!;
        Assert.Equal("Finalized", done.Status);
        Assert.Equal(["New", "Confirmed", "InPreparation", "Ready", "Delivered", "Finalized"], done.History.Select(entry => entry.Status));
    }

    [Fact]
    public async Task CancellationDoesNotDiscloseInternalReasonOrStaffIdentity()
    {
        using var guest = factory.CreateStaffClient();
        var (receipt, _) = await CreateAsync(guest);
        var user = await factory.CreateUserAsync();
        using var staff = factory.CreateStaffClient();
        var login = await staff.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        var id = Guid.Empty;
        await factory.WithDatabaseAsync(async database => id = (await database.Orders.SingleAsync()).Id);
        (await staff.PutAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Cancelled", 1, "Motivo interno privado"))).EnsureSuccessStatusCode();
        var text = await (await ReadAsync(guest, receipt.Tracking!.Token)).Content.ReadAsStringAsync();
        Assert.Contains("Cancelled", text);
        Assert.DoesNotContain("Motivo interno", text);
        Assert.DoesNotContain(user.Name, text);
        Assert.DoesNotContain(user.Id.ToString(), text);
    }

    [Fact]
    public async Task TrackingLimitIsIndependentFromCheckoutAndAppliesToInvalidAccess()
    {
        using var client = factory.CreateStaffClient();
        for (var index = 0; index < 60; index++)
            Assert.Equal(HttpStatusCode.NotFound, (await ReadAsync(client, "invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ReadAsync(client, "invalid")).StatusCode);
        await CreateAsync(client);
        using var other = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.NotFound, (await ReadAsync(other, "invalid")).StatusCode);
    }
}
