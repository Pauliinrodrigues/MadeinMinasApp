using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Dispatch;
using MadeInMinas.Api.DTOs.Kitchen;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.DTOs.Payments;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class PublicDeliveryTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private Product burger = null!;
    private IConfiguration Configuration => factory.Services.GetRequiredService<IConfiguration>();
    private const string AreaKey = "PublicDelivery:Areas:0:";

    public async Task InitializeAsync()
    {
        Configuration[AreaKey + "Id"] = "test-center";
        Configuration[AreaKey + "Neighborhood"] = "Centro de teste";
        Configuration[AreaKey + "City"] = "Cidade de teste";
        Configuration[AreaKey + "State"] = "MG";
        Configuration[AreaKey + "Fee"] = "5.50";
        await factory.WithDatabaseAsync(async database =>
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
    }

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

    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);
    private PublicCheckoutRequest Input() => new("Maria", "31999991234", new([new(burger.Id, 2)]))
    {
        Fulfillment = "Delivery",
        Address = new("test-center", " Rua informada ", " s/n ", " Casa 2 ", "30100-000", " Portão azul ")
    };

    private async Task<PublicOrderRequest> ReviewAsync(HttpClient client, PublicCheckoutRequest? input = null)
    {
        input ??= Input();
        var response = await client.PostAsJsonAsync("/api/public-checkout/review", input);
        response.EnsureSuccessStatusCode();
        var review = (await response.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        Assert.Equal("Delivery", review.Fulfillment);
        Assert.Equal(65.30m, review.Total);
        Assert.Equal(5.50m, review.DeliveryFee);
        Assert.Equal("Cidade de teste", review.Address!.City);
        Assert.Null(review.Address.Id);
        return new(Guid.NewGuid(), review.ReviewToken, input);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(code, JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task AreasAndReviewAreAnonymousReadOnlyAndUseServerAmounts()
    {
        using var client = factory.CreateStaffClient();
        var response = await client.GetAsync("/api/public-checkout/delivery-areas");
        Assert.True(response.Headers.CacheControl?.NoStore);
        var areas = (await response.Content.ReadFromJsonAsync<PublicDeliveryAreaResponse[]>())!;
        Assert.Equal(5.50m, Assert.Single(areas).Fee);
        await ReviewAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.Customers.AnyAsync());
            Assert.False(await database.Addresses.AnyAsync());
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("street")]
    [InlineData("number")]
    [InlineData("postal")]
    [InlineData("length")]
    [InlineData("area")]
    [InlineData("fee")]
    [InlineData("city")]
    [InlineData("customerAddress")]
    [InlineData("pickupAddress")]
    [InlineData("fulfillment")]
    public async Task InvalidOrForgedAddressIsRejectedBeforeWriting(string condition)
    {
        using var client = factory.CreateStaffClient();
        var input = JsonSerializer.SerializeToNode(Input(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        switch (condition)
        {
            case "missing":
                input["address"] = null;
                break;
            case "street":
                input["address"]!["street"] = "  ";
                break;
            case "number":
                input["address"]!["number"] = "";
                break;
            case "postal":
                input["address"]!["postalCode"] = "123";
                break;
            case "length":
                input["address"]!["reference"] = new string('a', 251);
                break;
            case "area":
                input["address"]!["areaId"] = "";
                break;
            case "fee":
                input["deliveryFee"] = 0;
                break;
            case "city":
                input["address"]!["city"] = "Outra cidade";
                break;
            case "customerAddress":
                input["address"]!["id"] = Guid.NewGuid();
                break;
            case "pickupAddress":
                input["fulfillment"] = "Pickup";
                break;
            case "fulfillment":
                input["fulfillment"] = null;
                break;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/public-checkout/review", input)).StatusCode);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.Orders.AnyAsync()));
    }

    [Fact]
    public async Task UnservedAreaDoesNotCreateCustomerOrOrder()
    {
        using var client = factory.CreateStaffClient();
        var input = Input() with { Address = Input().Address! with { AreaId = "outside" } };
        var request = new PublicOrderRequest(Guid.NewGuid(), new string('A', 64), input);
        await ProblemAsync(await client.PostAsJsonAsync("/api/public-checkout/orders", request), "PublicDeliveryUnavailable");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.False(await database.Customers.AnyAsync());
            Assert.False(await database.Orders.AnyAsync());
        });
    }

    [Theory]
    [InlineData("fee")]
    [InlineData("coverage")]
    [InlineData("address")]
    [InlineData("city")]
    [InlineData("fulfillment")]
    public async Task ChangesAfterReviewRequireNewReviewAndRollbackCustomer(string change)
    {
        using var client = factory.CreateStaffClient();
        var request = await ReviewAsync(client);
        if (change == "fee")
            Configuration[AreaKey + "Fee"] = "9.00";
        if (change == "coverage")
            Configuration[AreaKey + "Id"] = "another-area";
        if (change == "city")
            Configuration[AreaKey + "City"] = "Cidade alterada";
        if (change == "address")
            request = request with { Checkout = request.Checkout with { Address = request.Checkout.Address! with { Number = "33" } } };
        if (change == "fulfillment")
            request = request with { Checkout = request.Checkout with { Fulfillment = "Pickup", Address = null } };
        await ProblemAsync(await client.PostAsJsonAsync("/api/public-checkout/orders", request), change == "coverage" ? "PublicDeliveryUnavailable" : "OrderReviewChanged");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.False(await database.Customers.AnyAsync());
            Assert.False(await database.Orders.AnyAsync());
        });
    }

    [Fact]
    public async Task DeliverySavesSnapshotWithoutEditingCustomerAddressesAndReplaysAfterCoverageChanges()
    {
        var customer = new Customer { Name = "Nome reservado", NormalizedName = "NOME RESERVADO", Phone = "+5531999991234" };
        await factory.WithDatabaseAsync(async database =>
        {
            database.Customers.Add(customer);
            database.Addresses.Add(new Address { CustomerId = customer.Id, Street = "Rua privada", Number = "1", Neighborhood = "Privado", City = "Cidade privada", State = "MG" });
            await database.SaveChangesAsync();
        });
        using var client = factory.CreateStaffClient();
        var request = await ReviewAsync(client);
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/public-checkout/orders", request), client.PostAsJsonAsync("/api/public-checkout/orders", request));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var receipt = (await responses[0].Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Assert.Equal("Delivery", receipt.Fulfillment);
        Assert.DoesNotContain("Rua", await responses[0].Content.ReadAsStringAsync());
        Configuration[AreaKey + "Id"] = "another-area";
        Configuration[AreaKey + "Fee"] = "12";
        var replay = await client.PostAsJsonAsync("/api/public-checkout/orders", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt.Number, (await replay.Content.ReadFromJsonAsync<PublicOrderReceipt>())!.Number);
        var changed = request with { Checkout = request.Checkout with { Address = request.Checkout.Address! with { Reference = "Outro portão" } } };
        await ProblemAsync(await client.PostAsJsonAsync("/api/public-checkout/orders", changed), "OrderRequestConflict");
        await factory.WithDatabaseAsync(async database =>
        {
            var order = await database.Orders.SingleAsync();
            Assert.Equal("New", order.Status);
            Assert.Equal("Pending", order.StockStatus);
            Assert.Null(order.AddressId);
            Assert.Equal("Rua informada", order.AddressStreet);
            Assert.Equal("s/n", order.AddressNumber);
            Assert.Equal("Casa 2", order.AddressComplement);
            Assert.Equal("30100000", order.AddressPostalCode);
            Assert.Equal("Portão azul", order.AddressReference);
            Assert.Equal(5.50m, order.DeliveryFee);
            Assert.Equal(65.30m, order.Total);
            Assert.Equal("Rua privada", (await database.Addresses.SingleAsync()).Street);
            Assert.Equal("Nome reservado", (await database.Customers.SingleAsync()).Name);
            Assert.False(await database.Payments.AnyAsync());
            Assert.False(await database.StockMovements.AnyAsync());
        });
    }

    [Fact]
    public async Task DeliveryPassesThroughConfirmationKitchenDispatchPaymentAndTracking()
    {
        using var guest = factory.CreateStaffClient();
        var creation = await guest.PostAsJsonAsync("/api/public-checkout/orders", await ReviewAsync(guest));
        creation.EnsureSuccessStatusCode();
        var receipt = (await creation.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Guid id = default;
        await factory.WithDatabaseAsync(async database => id = (await database.Orders.SingleAsync()).Id);
        var user = await factory.CreateUserAsync();
        using var staff = factory.CreateStaffClient();
        var login = await staff.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        var detail = (await staff.GetFromJsonAsync<OrderResponse>($"/api/orders/{id}"))!;
        Assert.Equal("Rua informada", detail.Address!.Street);
        Assert.Null(detail.Address.Id);
        Configuration[AreaKey + "Id"] = "another-area";
        (await staff.PutAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Confirmed", 1))).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/kitchen/orders/{id}/status", new KitchenStatusRequest("InPreparation", 2))).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/kitchen/orders/{id}/status", new KitchenStatusRequest("Ready", 3))).EnsureSuccessStatusCode();
        var queue = (await staff.GetFromJsonAsync<DispatchPageResponse>("/api/dispatch/orders?status=Ready"))!;
        Assert.Equal("Rua informada", Assert.Single(queue.Items).Address!.Street);
        Assert.Contains("Rua informada", await staff.GetStringAsync($"/api/print/orders/{id}/dispatch"));
        Assert.DoesNotContain("Rua informada", await staff.GetStringAsync($"/api/print/orders/{id}/kitchen"));
        var version = 4;
        foreach (var status in new[] { "AwaitingDelivery", "OutForDelivery", "Delivered" })
            (await staff.PutAsJsonAsync($"/api/dispatch/orders/{id}/status", new DispatchStatusRequest(status, version++))).EnsureSuccessStatusCode();
        await ProblemAsync(await staff.PutAsJsonAsync($"/api/dispatch/orders/{id}/status", new DispatchStatusRequest("Finalized", version)), "OrderPaymentRequired");
        var paymentResponse = await staff.PostAsJsonAsync($"/api/orders/{id}/payments", new CreatePaymentRequest(Guid.NewGuid(), "Pix", version));
        paymentResponse.EnsureSuccessStatusCode();
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;
        Assert.Equal(65.30m, payment.Amount);
        (await staff.PutAsJsonAsync($"/api/orders/{id}/payments/{payment.Id}/receive", new ReceivePaymentRequest(1, true))).EnsureSuccessStatusCode();
        (await staff.PutAsJsonAsync($"/api/dispatch/orders/{id}/status", new DispatchStatusRequest("Finalized", version))).EnsureSuccessStatusCode();
        guest.DefaultRequestHeaders.Add("X-Order-Access", receipt.Tracking!.Token);
        var tracking = await guest.GetStringAsync("/api/public-orders/tracking");
        Assert.Contains("Finalized", tracking);
        Assert.Contains("Delivery", tracking);
        Assert.DoesNotContain("Rua informada", tracking);
        await factory.WithDatabaseAsync(async database => Assert.Equal("Consumed", (await database.Orders.SingleAsync()).StockStatus));
    }

    [Fact]
    public async Task DatabaseRejectsMissingSnapshotAndMigrationRollbackPreservesDeliveries()
    {
        using var client = factory.CreateStaffClient();
        (await client.PostAsJsonAsync("/api/public-checkout/orders", await ReviewAsync(client))).EnsureSuccessStatusCode();
        await factory.WithDatabaseAsync(async database =>
        {
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => database.Orders.ExecuteUpdateAsync(update => update.SetProperty(order => order.AddressStreet, (string?)null)));
            Assert.Equal("CK_Orders_Fulfillment", invalid.ConstraintName);
            var migrations = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
            var script = database.GetService<IMigrator>().GenerateScript(migrations[^1], migrations[^2], MigrationsSqlGenerationOptions.NoTransactions);
            await using var transaction = await database.Database.BeginTransactionAsync();
            var rollback = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(script));
            Assert.Contains("Public deliveries exist", rollback.MessageText);
            await transaction.RollbackAsync();
            Assert.Equal("Delivery", (await database.Orders.SingleAsync()).Fulfillment);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfirmationStillRejectsInactiveCustomerOrProduct(bool customerInactive)
    {
        using var guest = factory.CreateStaffClient();
        (await guest.PostAsJsonAsync("/api/public-checkout/orders", await ReviewAsync(guest))).EnsureSuccessStatusCode();
        Guid id = default;
        await factory.WithDatabaseAsync(async database =>
        {
            id = (await database.Orders.SingleAsync()).Id;
            if (customerInactive)
                await database.Customers.ExecuteUpdateAsync(update => update.SetProperty(customer => customer.IsActive, false));
            else
                await database.Products.ExecuteUpdateAsync(update => update.SetProperty(product => product.IsAvailable, false));
        });
        var user = await factory.CreateUserAsync();
        using var staff = factory.CreateStaffClient();
        var login = await staff.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        await ProblemAsync(await staff.PutAsJsonAsync($"/api/orders/{id}/status", new OrderStatusRequest("Confirmed", 1)),
            customerInactive ? "CartCustomerUnavailable" : "CartProductUnavailable");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal("New", (await database.Orders.SingleAsync()).Status);
            Assert.False(await database.StockMovements.AnyAsync());
        });
    }

    [Fact]
    public async Task PreviousPickupPayloadCanBeReplayedWithExplicitDefaultFulfillment()
    {
        using var client = factory.CreateStaffClient();
        var input = Input() with { Fulfillment = "Pickup", Address = null };
        var legacy = JsonSerializer.SerializeToNode(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        legacy.AsObject().Remove("fulfillment");
        legacy.AsObject().Remove("address");
        var reviewResponse = await client.PostAsJsonAsync("/api/public-checkout/review", legacy);
        reviewResponse.EnsureSuccessStatusCode();
        var review = (await reviewResponse.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        var id = Guid.NewGuid();
        var creation = await client.PostAsJsonAsync("/api/public-checkout/orders", new { requestId = id, reviewToken = review.ReviewToken, checkout = legacy });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var replay = await client.PostAsJsonAsync("/api/public-checkout/orders", new PublicOrderRequest(id, review.ReviewToken, input));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await creation.Content.ReadFromJsonAsync<PublicOrderReceipt>())!.Number, (await replay.Content.ReadFromJsonAsync<PublicOrderReceipt>())!.Number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("10000")]
    public void ConfigurationRejectsMissingOrInvalidFees(string? value)
    {
        var area = new PublicDeliveryArea
        {
            Id = "test",
            Neighborhood = "Centro",
            City = "Cidade",
            State = "MG",
            Fee = value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
        };
        Assert.False(new PublicDeliveryOptions { Areas = [area] }.IsValid());
    }

    [Fact]
    public void ConfigurationAllowsExplicitFreeDeliveryAndEmptyCoverageButRejectsDuplicates()
    {
        var area = new PublicDeliveryArea { Id = "test", Neighborhood = "Centro", City = "Cidade", State = "MG", Fee = 0 };
        Assert.True(new PublicDeliveryOptions().IsValid());
        Assert.True(new PublicDeliveryOptions { Areas = [area] }.IsValid());
        Assert.False(new PublicDeliveryOptions { Areas = [area, area] }.IsValid());
    }
}
