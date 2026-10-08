using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.Controllers;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Kitchen;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class PrintQueueTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Customer customer = new() { Name = "Cliente reservado", NormalizedName = "CLIENTE RESERVADO", Phone = "+5531999991234" };
    private Product product = null!;
    public Task InitializeAsync() => factory.WithDatabaseAsync(async db =>
    {
        await db.StockMovements.ExecuteDeleteAsync();
        await db.Payments.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();
        await db.RecipeItems.ExecuteDeleteAsync();
        await db.Recipes.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();
        await db.Addresses.ExecuteDeleteAsync();
        await db.Customers.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();
        await db.PrintStations.ExecuteUpdateAsync(update => update.SetProperty(s => s.KeyHash, (string?)null)
            .SetProperty(s => s.Automatic, false).SetProperty(s => s.Version, 1).SetProperty(s => s.LastSeenAt, (DateTimeOffset?)null));
        product = new Product
        {
            Name = "Uai Sô",
            NormalizedName = "UAI SÔ",
            Price = 29.90m,
            IsActive = true,
            IsAvailable = true,
            Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" }
        };
        db.Products.Add(product);
        OrderStockFixture.AddRecipe(db, product);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
    });
    public Task DisposeAsync() => factory.WithDatabaseAsync(async db =>
    {
        await db.PrintStations.ExecuteUpdateAsync(update => update.SetProperty(s => s.Automatic, false).SetProperty(s => s.KeyHash, (string?)null));
        await db.PrintJobs.ExecuteDeleteAsync();
        await db.StockMovements.ExecuteDeleteAsync();
        await db.Payments.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();
    });
    private async Task<HttpClient> Login(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }
    private async Task<(HttpClient Agent, string Key)> Register(HttpClient admin)
    {
        var settings = (await admin.GetFromJsonAsync<PrintSettingsResponse>("/api/printing/settings"))!;
        var response = await admin.PostAsJsonAsync("/api/printing/station", new PrintingVersionRequest(settings.Version));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = json.RootElement.GetProperty("token").GetString()!;
        var agent = factory.CreateStaffClient();
        agent.DefaultRequestHeaders.Add("X-Print-Key", key);
        return (agent, key);
    }
    private static async Task Activate(HttpClient admin, HttpClient agent)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsync("/api/printing/agent/claim", null)).StatusCode);
        var settings = (await admin.GetFromJsonAsync<PrintSettingsResponse>("/api/printing/settings"))!;
        (await admin.PutAsJsonAsync("/api/printing/settings", new PrintingAutomaticRequest(true, settings.Version))).EnsureSuccessStatusCode();
    }
    private async Task<OrderResponse> Create(HttpClient client)
    {
        var cart = new CartQuoteRequest(customer.Id, "Pickup", [new(product.Id, 2, "Sem cebola")], null, "Embalar separado");
        var quoteResponse = await client.PostAsJsonAsync("/api/cart/quote", cart);
        quoteResponse.EnsureSuccessStatusCode();
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<CartQuoteResponse>())!;
        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(), quote.ReviewToken, cart));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
    private static async Task Confirm(HttpClient client, Guid id) =>
        (await client.PutAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Confirmed", 1))).EnsureSuccessStatusCode();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorConfiguresStation(int role)
    {
        using var employee = await Login(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/printing/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/printing/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/printing/station", new PrintingVersionRequest(1))).StatusCode);
        using var anonymous = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/printing/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/printing/agent/claim", null)).StatusCode);
    }

    [Fact]
    public async Task ActivationHasNoBacklogAndTransitionsEnqueueOnePrivateKitchenAndOneDispatchSnapshot()
    {
        using var admin = await Login();
        var old = await Create(admin);
        await Confirm(admin, old.Id);
        var (agent, _) = await Register(admin);
        using (agent)
        {
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/printing/settings", new PrintingAutomaticRequest(true, 2))).StatusCode);
            await Activate(admin, agent);
            Assert.Empty((await admin.GetFromJsonAsync<PrintJobResponse[]>("/api/printing/jobs"))!);
            var order = await Create(admin);
            await Confirm(admin, order.Id);
            await Confirm(admin, order.Id);
            (await admin.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("InPreparation", 2))).EnsureSuccessStatusCode();
            (await admin.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("Ready", 3))).EnsureSuccessStatusCode();
            (await admin.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("Ready", 3))).EnsureSuccessStatusCode();
            var jobs = (await admin.GetFromJsonAsync<PrintJobResponse[]>("/api/printing/jobs"))!;
            Assert.Equal(2, jobs.Length);
            await factory.WithDatabaseAsync(async db =>
            {
                var kitchen = await db.PrintJobs.SingleAsync(j => j.Mode == "kitchen");
                var dispatch = await db.PrintJobs.SingleAsync(j => j.Mode == "dispatch");
                var production = string.Join('\n', JsonSerializer.Deserialize<string[]>(kitchen.Payload)!);
                Assert.Contains("Sem cebola", production);
                Assert.DoesNotContain(customer.Name, production);
                Assert.DoesNotContain(customer.Phone, production);
                Assert.DoesNotContain("R$", production);
                var shipping = string.Join('\n', JsonSerializer.Deserialize<string[]>(dispatch.Payload)!);
                Assert.Contains(customer.Name, shipping);
                Assert.Contains("59,80", shipping);
                Assert.Contains("PAGAMENTO NÃO REGISTRADO", shipping);
                await db.Customers.Where(c => c.Id == customer.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Name, "Nome alterado"));
                Assert.Equal(dispatch.Payload, (await db.PrintJobs.AsNoTracking().SingleAsync(j => j.Id == dispatch.Id)).Payload);
            });
        }
    }

    [Fact]
    public async Task ManualRetryIsIdempotentAndRequiresCurrentVersionAndModePermission()
    {
        using var admin = await Login();
        var order = await Create(admin);
        var input = new PrintManualRequest(Guid.NewGuid(), 1);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/printing/orders/{order.Id}/kitchen", input)).StatusCode);
        var (agent, _) = await Register(admin);
        using (agent)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.PostAsJsonAsync($"/api/printing/orders/{order.Id}/kitchen", input)));
            foreach (var response in responses)
                response.EnsureSuccessStatusCode();
            Assert.Equal((await responses[0].Content.ReadFromJsonAsync<PrintJobResponse>())!.Id, (await responses[1].Content.ReadFromJsonAsync<PrintJobResponse>())!.Id);
            await Confirm(admin, order.Id);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/printing/orders/{order.Id}/kitchen", new PrintManualRequest(Guid.NewGuid(), 1))).StatusCode);
            using var dispatch = await Login(4);
            Assert.Equal(HttpStatusCode.Forbidden, (await dispatch.PostAsJsonAsync($"/api/printing/orders/{order.Id}/kitchen", new PrintManualRequest(Guid.NewGuid(), 2))).StatusCode);
            (await dispatch.PostAsJsonAsync($"/api/printing/orders/{order.Id}/dispatch", new PrintManualRequest(Guid.NewGuid(), 2))).EnsureSuccessStatusCode();
            Assert.Equal(2, (await admin.GetFromJsonAsync<PrintJobResponse[]>("/api/printing/jobs"))!.Length);
        }
    }

    [Fact]
    public async Task ConcurrentAgentsCannotDuplicateAndExpiredClaimIsReviewedInsteadOfReprinted()
    {
        using var admin = await Login();
        var (agent, key) = await Register(admin);
        using (agent)
        {
            await Activate(admin, agent);
            var order = await Create(admin);
            await Confirm(admin, order.Id);
            using var second = factory.CreateStaffClient();
            second.DefaultRequestHeaders.Add("X-Print-Key", key);
            var responses = await Task.WhenAll(agent.PostAsync("/api/printing/agent/claim", null), second.PostAsync("/api/printing/agent/claim", null));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
            var claim = (await responses.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<PrintClaimResponse>())!;
            await factory.WithDatabaseAsync(async db => await db.PrintJobs.Where(j => j.Id == claim.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(j => j.ClaimedAt, DateTimeOffset.UtcNow.AddMinutes(-3))));
            Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsync("/api/printing/agent/claim", null)).StatusCode);
            Assert.Equal("Review", (await admin.GetFromJsonAsync<PrintJobResponse[]>("/api/printing/jobs"))!.Single().State);
            Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync($"/api/printing/agent/jobs/{claim.Id}/finish", new PrintFinishRequest(Guid.NewGuid(), true))).StatusCode);
            (await agent.PostAsJsonAsync($"/api/printing/agent/jobs/{claim.Id}/finish", new PrintFinishRequest(claim.ClaimId, true))).EnsureSuccessStatusCode();
            (await agent.PostAsJsonAsync($"/api/printing/agent/jobs/{claim.Id}/finish", new PrintFinishRequest(claim.ClaimId, false))).EnsureSuccessStatusCode();
            Assert.Equal("Submitted", (await admin.GetFromJsonAsync<PrintJobResponse[]>("/api/printing/jobs"))!.Single().State);
            var replacement = await Register(admin);
            replacement.Agent.Dispose();
            Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsync("/api/printing/agent/claim", null)).StatusCode);
        }
    }

    [Fact]
    public async Task FailedTransitionDoesNotEnqueueAndCancellationRemovesWaitingReceipt()
    {
        using var admin = await Login();
        var (agent, _) = await Register(admin);
        using (agent)
        {
            await Activate(admin, agent);
            var order = await Create(admin);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Confirmed", 50))).StatusCode);
            Assert.Empty((await admin.GetFromJsonAsync<PrintJobResponse[]>("/api/printing/jobs"))!);
            await Confirm(admin, order.Id);
            (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", new OrderStatusRequest("Cancelled", 2, "Cliente desistiu"))).EnsureSuccessStatusCode();
            Assert.Equal("Cancelled", (await admin.GetFromJsonAsync<PrintJobResponse[]>("/api/printing/jobs"))!.Single().State);
            Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsync("/api/printing/agent/claim", null)).StatusCode);
        }
    }
}
