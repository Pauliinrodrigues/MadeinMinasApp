using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class PublicCheckoutTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private const string ReviewPath = "/api/public-checkout/review";
    private const string OrdersPath = "/api/public-checkout/orders";
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
    private PublicCheckoutRequest Input() => new("  Maria visitante  ", "(31) 99999-1234", new([new(burger.Id, 2, "  Sem cebola  ")], "  Embalar separado  "));

    private async Task<PublicOrderRequest> ReviewAsync(HttpClient client, PublicCheckoutRequest? input = null)
    {
        input ??= Input();
        var response = await client.PostAsJsonAsync(ReviewPath, input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var quote = (await response.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        Assert.Equal("Pickup", quote.Fulfillment);
        Assert.Equal(0, quote.DeliveryFee);
        Assert.Equal(quote.Subtotal, quote.Total);
        return new PublicOrderRequest(Guid.NewGuid(), quote.ReviewToken, input);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReviewIsAnonymousReadOnlyAndDoesNotLookUpCustomerData()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            database.Customers.Add(new Customer { Name = "Nome privado", NormalizedName = "NOME PRIVADO", Phone = "+5531999991234" });
            await database.SaveChangesAsync();
        });
        using var client = factory.CreateStaffClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        var response = await client.PostAsJsonAsync(ReviewPath, Input());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = (await response.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        Assert.Equal("Maria visitante", quote.Name);
        Assert.Equal("+5531999991234", quote.Phone);
        Assert.Equal(59.80m, quote.Total);
        Assert.Equal("Sem cebola", quote.Items[0].Notes);
        Assert.DoesNotContain("Nome privado", await response.Content.ReadAsStringAsync());
        var normalized = await ReviewAsync(client, Input() with { Name = "Maria visitante", Phone = "+5531999991234" });
        Assert.Equal(quote.ReviewToken, normalized.ReviewToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(1, await database.Customers.CountAsync());
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.Payments.AnyAsync());
            Assert.False(await database.StockMovements.AnyAsync());
        });
    }

    [Fact]
    public async Task CreationStoresPublicOriginSnapshotsAndHistoryWithoutPaymentOrStockEffects()
    {
        using var client = factory.CreateStaffClient();
        var request = await ReviewAsync(client);
        var response = await client.PostAsJsonAsync(OrdersPath, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["createdAt", "fulfillment", "number", "total", "tracking"], json.RootElement.EnumerateObject().Select(field => field.Name).Order());
        var receipt = (await response.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Assert.True(receipt.Number > 0);
        Assert.Equal(59.80m, receipt.Total);
        await factory.WithDatabaseAsync(async database =>
        {
            var order = await database.Orders.Include(order => order.Items).Include(order => order.History).SingleAsync();
            Assert.Equal("DirectLink", order.Origin);
            Assert.Equal("New", order.Status);
            Assert.Equal("Pending", order.StockStatus);
            Assert.Null(order.CreatedById);
            Assert.Null(order.AddressId);
            Assert.Equal(0, order.DeliveryFee);
            Assert.Equal("Maria visitante", order.CustomerName);
            Assert.Equal("+5531999991234", order.CustomerPhone);
            Assert.Equal("Uai Sô", order.Items.Single().ProductName);
            Assert.Equal("Sem cebola", order.Items.Single().Notes);
            Assert.Equal("Embalar separado", order.Notes);
            Assert.Null(order.History.Single().ActorId);
            Assert.Equal("Cliente pelo site", order.History.Single().ActorName);
            Assert.Equal(order.CreatedAt, order.History.Single().OccurredAt);
            Assert.Equal(receipt.CreatedAt, order.CreatedAt);
            Assert.Equal(order.CustomerId, (await database.Customers.SingleAsync()).Id);
            Assert.False(await database.Payments.AnyAsync());
            Assert.False(await database.StockMovements.AnyAsync());
            Assert.Equal(10000m, (await database.Ingredients.SingleAsync()).CurrentStock);
        });
    }

    [Fact]
    public async Task ExistingCustomerIsLinkedWithoutOverwritingNameOrReturningPrivateData()
    {
        var customer = new Customer { Name = "Nome privado", NormalizedName = "NOME PRIVADO", Phone = "+5531999991234" };
        await factory.WithDatabaseAsync(async database => { database.Customers.Add(customer); await database.SaveChangesAsync(); });
        using var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync(OrdersPath, await ReviewAsync(client));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("Nome privado", await response.Content.ReadAsStringAsync());
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal("Nome privado", (await database.Customers.SingleAsync()).Name);
            var order = await database.Orders.SingleAsync();
            Assert.Equal(customer.Id, order.CustomerId);
            Assert.Equal("Maria visitante", order.CustomerName);
        });
    }

    [Theory]
    [InlineData("price")]
    [InlineData("productName")]
    [InlineData("name")]
    [InlineData("phone")]
    [InlineData("quantity")]
    [InlineData("notes")]
    [InlineData("token")]
    public async Task ChangedReviewRollsBackOrderAndNewCustomer(string change)
    {
        using var client = factory.CreateStaffClient();
        var request = await ReviewAsync(client);
        if (change is "price" or "productName")
            await factory.WithDatabaseAsync(async database =>
            {
                var product = await database.Products.SingleAsync();
                if (change == "price")
                    product.Price = 32.25m;
                else
                    product.Name = "Novo nome";
                await database.SaveChangesAsync();
            });
        if (change == "name")
            request = request with { Checkout = request.Checkout with { Name = "Outro nome" } };
        if (change == "phone")
            request = request with { Checkout = request.Checkout with { Phone = "31988881234" } };
        if (change == "quantity")
            request = request with { Checkout = request.Checkout with { Cart = new([new(burger.Id, 3)]) } };
        if (change == "notes")
            request = request with { Checkout = request.Checkout with { Cart = request.Checkout.Cart with { Notes = "Outra observação" } } };
        if (change == "token")
            request = request with { ReviewToken = new string('0', 64) };
        await ProblemAsync(await client.PostAsJsonAsync(OrdersPath, request), "OrderReviewChanged");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.Customers.AnyAsync());
            Assert.False(await database.OrderStatusHistory.AnyAsync());
        });
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("inactive")]
    [InlineData("category")]
    public async Task UnavailableProductRejectsCreationAtomically(string condition)
    {
        using var client = factory.CreateStaffClient();
        var request = await ReviewAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            if (condition == "paused")
                await database.Products.ExecuteUpdateAsync(update => update.SetProperty(product => product.IsAvailable, false));
            if (condition == "inactive")
                await database.Products.ExecuteUpdateAsync(update => update.SetProperty(product => product.IsActive, false));
            if (condition == "category")
                await database.Categories.ExecuteUpdateAsync(update => update.SetProperty(category => category.IsActive, false));
        });
        await ProblemAsync(await client.PostAsJsonAsync(OrdersPath, request), "CartProductUnavailable");
        await factory.WithDatabaseAsync(async database => { Assert.False(await database.Orders.AnyAsync()); Assert.False(await database.Customers.AnyAsync()); });
    }

    [Fact]
    public async Task InactiveCustomerIsNotReactivatedAndItsDetailsAreNotDisclosed()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            database.Customers.Add(new Customer { Name = "Nome privado", NormalizedName = "NOME PRIVADO", Phone = "+5531999991234", IsActive = false });
            await database.SaveChangesAsync();
        });
        using var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync(OrdersPath, await ReviewAsync(client));
        await ProblemAsync(response, "PublicCheckoutUnavailable");
        Assert.DoesNotContain("Nome privado", await response.Content.ReadAsStringAsync());
        await factory.WithDatabaseAsync(async database => { Assert.False((await database.Customers.SingleAsync()).IsActive); Assert.False(await database.Orders.AnyAsync()); });
    }

    [Fact]
    public async Task ConcurrentRetriesCreateOnlyOneOrderAndReplaySurvivesCatalogChanges()
    {
        using var client = factory.CreateStaffClient();
        var input = await ReviewAsync(client);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync(OrdersPath, input)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var receipts = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<PublicOrderReceipt>()));
        var receipt = receipts[0]!;
        Assert.All(receipts, result =>
        {
            Assert.NotNull(result!.Tracking);
            Assert.Equal(receipt.Tracking!.ExpiresAt, result.Tracking.ExpiresAt);
            Assert.Equal(receipt with { Tracking = result.Tracking }, result);
        });
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(1, await database.Orders.CountAsync());
            Assert.Equal(1, await database.Customers.CountAsync());
            Assert.Equal(1, await database.OrderStatusHistory.CountAsync());
            await database.Products.ExecuteUpdateAsync(update => update.SetProperty(product => product.IsActive, false).SetProperty(product => product.Price, 49m));
        });
        var replay = await client.PostAsJsonAsync(OrdersPath, input);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var recovered = (await replay.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Assert.Equal(receipt.Tracking!.ExpiresAt, recovered.Tracking!.ExpiresAt);
        Assert.Equal(receipt with { Tracking = recovered.Tracking }, recovered);
        await ProblemAsync(await client.PostAsJsonAsync(OrdersPath, input with { Checkout = input.Checkout with { Name = "Outra pessoa" } }), "OrderRequestConflict");
    }

    [Fact]
    public async Task DifferentAttemptsForTheSamePhoneShareCustomerAndHaveUniqueNumbers()
    {
        using var client = factory.CreateStaffClient();
        var input = await ReviewAsync(client);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync(OrdersPath, input with { RequestId = Guid.NewGuid() })));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(1, await database.Customers.CountAsync());
            Assert.Equal(4, await database.Orders.Select(order => order.Number).Distinct().CountAsync());
        });
    }

    [Theory]
    [InlineData("name")]
    [InlineData("phone")]
    [InlineData("cart")]
    [InlineData("nullItem")]
    [InlineData("unitPrice")]
    [InlineData("customerId")]
    [InlineData("fulfillment")]
    [InlineData("deliveryFee")]
    [InlineData("status")]
    [InlineData("origin")]
    [InlineData("requestId")]
    [InlineData("emptyRequestId")]
    [InlineData("reviewToken")]
    public async Task InvalidAndTamperedRequestsAreRejectedBeforeWriting(string condition)
    {
        using var client = factory.CreateStaffClient();
        var input = JsonSerializer.SerializeToNode(await ReviewAsync(client), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var checkout = input["checkout"]!;
        switch (condition)
        {
            case "name":
                checkout["name"] = "   ";
                break;
            case "phone":
                checkout["phone"] = "123";
                break;
            case "cart":
                checkout["cart"] = null;
                break;
            case "nullItem":
                checkout["cart"]!["items"] = new JsonArray((JsonNode?)null);
                break;
            case "unitPrice":
                checkout["cart"]!["items"]![0]!["unitPrice"] = 0.01;
                break;
            case "customerId":
                checkout["customerId"] = Guid.NewGuid().ToString();
                break;
            case "fulfillment":
                checkout["fulfillment"] = "Delivery";
                break;
            case "deliveryFee":
                checkout["cart"]!["deliveryFee"] = 0;
                break;
            case "status":
                input["status"] = "Confirmed";
                break;
            case "origin":
                input["origin"] = "Manual";
                break;
            case "requestId":
                input["requestId"] = null;
                break;
            case "emptyRequestId":
                input["requestId"] = Guid.Empty.ToString();
                break;
            case "reviewToken":
                input["reviewToken"] = "invalid";
                break;
        }
        var response = await client.PostAsJsonAsync(OrdersPath, input);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.WithDatabaseAsync(async database => { Assert.False(await database.Orders.AnyAsync()); Assert.False(await database.Customers.AnyAsync()); });
    }

    [Fact]
    public async Task StaffCanReadConfirmAndCancelThePublicOrderWithStockAndHistory()
    {
        using var guest = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.Created, (await guest.PostAsJsonAsync(OrdersPath, await ReviewAsync(guest))).StatusCode);
        Guid id = default;
        await factory.WithDatabaseAsync(async database => id = (await database.Orders.SingleAsync()).Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/orders/{id}")).StatusCode);
        var user = await factory.CreateUserAsync();
        using var staff = factory.CreateStaffClient();
        var login = await staff.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        var detail = (await staff.GetFromJsonAsync<OrderResponse>($"/api/orders/{id}"))!;
        Assert.Equal("DirectLink", detail.Origin);
        Assert.Null(detail.History.Single().ActorId);
        var confirmed = await staff.PutAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Confirmed", 1));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Consumed", (await confirmed.Content.ReadFromJsonAsync<OrderResponse>())!.StockStatus);
        var cancelled = await staff.PutAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Cancelled", 2, "Teste de cancelamento"));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var result = (await cancelled.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal("Returned", result.StockStatus);
        Assert.Equal(3, result.History.Length);
        Assert.All(result.History.Skip(1), entry => Assert.Equal(user.Id, entry.ActorId));
        await factory.WithDatabaseAsync(async database => Assert.Equal(10000m, (await database.Ingredients.SingleAsync()).CurrentStock));
    }

    [Fact]
    public async Task DatabaseRequiresStaffForManualOrdersAndOperationalHistory()
    {
        using var guest = factory.CreateStaffClient();
        (await guest.PostAsJsonAsync(OrdersPath, await ReviewAsync(guest))).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database =>
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => database.Orders.ExecuteUpdateAsync(update => update.SetProperty(order => order.Origin, "Manual")));
            Assert.Equal("CK_Orders_Origin", failure.ConstraintName);
        });
        await factory.WithDatabaseAsync(async database =>
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => database.OrderStatusHistory.ExecuteUpdateAsync(update => update
                .SetProperty(history => history.Version, 2).SetProperty(history => history.FromStatus, "New").SetProperty(history => history.ToStatus, "Confirmed")));
            Assert.Equal("CK_OrderStatusHistory_Actor", failure.ConstraintName);
        });
    }

    [Fact]
    public async Task CheckoutRateLimitIsAppliedPerRemoteAddress()
    {
        using var client = factory.CreateStaffClient();
        for (var index = 0; index < 20; index++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(ReviewPath, Input())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(ReviewPath, Input())).StatusCode);
        using var other = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync(ReviewPath, Input())).StatusCode);
    }

    [Fact]
    public async Task MigrationRollbackRefusesToErasePublicAuthorship()
    {
        using var client = factory.CreateStaffClient();
        (await client.PostAsJsonAsync(OrdersPath, await ReviewAsync(client))).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database =>
        {
            var script = database.GetService<IMigrator>().GenerateScript("20261005145517_AddPublicOrders", "20261002181034_AddOrderStock", MigrationsSqlGenerationOptions.NoTransactions);
            await using var transaction = await database.Database.BeginTransactionAsync();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(script));
            Assert.Contains("Public orders exist", failure.MessageText);
            await transaction.RollbackAsync();
            Assert.Equal("DirectLink", (await database.Orders.SingleAsync()).Origin);
            Assert.Null((await database.OrderStatusHistory.SingleAsync()).ActorId);
        });
    }
}
