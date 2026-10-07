using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Delivery;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class DeliverySettingsTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private IConfiguration Configuration => factory.Services.GetRequiredService<IConfiguration>();
    private const string AreaKey = "PublicDelivery:Areas:0:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        Configuration["PublicDelivery:FixedFee"] = null;
        Configuration[AreaKey + "Id"] = "legacy-center";
        Configuration[AreaKey + "Neighborhood"] = "Centro de teste";
        Configuration[AreaKey + "City"] = "Cidade de teste";
        Configuration[AreaKey + "State"] = "MG";
        Configuration[AreaKey + "Fee"] = "5.50";
        Configuration[AreaKey + "CoversAllNeighborhoods"] = "false";
        await factory.WithDatabaseAsync(ClearAsync);
    }

    private static async Task ClearAsync(AppDbContext database)
    {
        await database.DeliverySettings.ExecuteDeleteAsync();
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

    private async Task<HttpClient> SignInAsync(int roleId = 1)
    {
        var user = await factory.CreateUserAsync(roleId);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(roleId == 1, login.User.Permissions.Contains("delivery.manage"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
    private static async Task<DeliverySettingsResponse> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/delivery-settings");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<DeliverySettingsResponse>())!;
    }
    private static SaveDeliverySettingsRequest Input(DeliverySettingsResponse current) => new(current.Revision,
        current.Areas.Select(area => new DeliveryAreaRequest(area.Id, area.Neighborhood, area.City, area.State, area.Fee, area.IsActive)
        { CoversAllNeighborhoods = area.CoversAllNeighborhoods }).ToArray());
    private static async Task<DeliverySettingsResponse> SaveAsync(HttpClient client, SaveDeliverySettingsRequest input)
    {
        var response = await client.PutAsJsonAsync("/api/delivery-settings", input);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeliverySettingsResponse>())!;
    }
    private static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task PublicOptionsExposeFixedFeeAndPickupAddressWithoutLoginOrWrites()
    {
        Configuration["PublicDelivery:FixedFee"] = "5.00";
        Configuration["PublicDelivery:PickupAddress"] = " Rua de teste, 68 — Bairro de teste ";
        using var guest = factory.CreateStaffClient();
        var response = await guest.GetAsync("/api/public-checkout/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var options = (await response.Content.ReadFromJsonAsync<PublicCheckoutOptionsResponse>())!;
        Assert.Equal(5m, options.FixedDeliveryFee);
        Assert.Equal(0m, options.PickupFee);
        Assert.Equal("Rua de teste, 68 — Bairro de teste", options.PickupAddress);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.False(await database.DeliverySettings.AnyAsync());
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.Customers.AnyAsync());
        });
    }

    [Fact]
    public async Task WholeCityCoveragePersistsAndCanBePausedWithoutLosingItsScope()
    {
        Configuration[AreaKey + "CoversAllNeighborhoods"] = "true";
        Configuration[AreaKey + "Neighborhood"] = "Todos os bairros";
        using var admin = await SignInAsync();
        var current = await ReadAsync(admin);
        Assert.True(Assert.Single(current.Areas).CoversAllNeighborhoods);
        current = await SaveAsync(admin, Input(current));
        Assert.True(Assert.Single(current.Areas).CoversAllNeighborhoods);
        var paused = Input(current);
        paused = paused with { Areas = paused.Areas.Select(area => area with { IsActive = false }).ToArray() };
        current = await SaveAsync(admin, paused);
        Assert.True(Assert.Single(current.Areas).CoversAllNeighborhoods);
        using var guest = factory.CreateStaffClient();
        Assert.Empty((await guest.GetFromJsonAsync<PublicDeliveryAreaResponse[]>("/api/public-checkout/delivery-areas"))!);
        current = await SaveAsync(admin, Input(current) with { Areas = Input(current).Areas.Select(area => area with { IsActive = true }).ToArray() });
        var active = Assert.Single((await guest.GetFromJsonAsync<PublicDeliveryAreaResponse[]>("/api/public-checkout/delivery-areas"))!);
        Assert.True(active.CoversAllNeighborhoods);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.Orders.AnyAsync()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task FixedFeeCannotBeOverriddenByRegionEdits(decimal fee)
    {
        Configuration["PublicDelivery:FixedFee"] = "5.00";
        using var admin = await SignInAsync();
        var current = await ReadAsync(admin);
        Assert.Equal(5m, current.FixedFee);
        Assert.Equal(5m, Assert.Single(current.Areas).Fee);
        var response = await admin.PutAsJsonAsync("/api/delivery-settings", Input(current) with
        {
            Areas = [Input(current).Areas[0] with { Fee = fee }]
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FixedDeliveryFeeRequired", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        Assert.Equal(current.Revision, (await ReadAsync(admin)).Revision);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.DeliverySettings.AnyAsync()));
    }

    [Fact]
    public async Task FixedFeeOverridesLegacyAndStoredRatesWhilePreservingRecordedOrders()
    {
        using var admin = await SignInAsync();
        var legacy = await ReadAsync(admin);
        await SaveAsync(admin, Input(legacy)); // A saved legacy region still has its original 5.50 rate.
        Configuration["PublicDelivery:FixedFee"] = "5.00";
        var current = await ReadAsync(admin);
        Assert.NotEqual(legacy.Revision, current.Revision);
        Assert.Equal(5m, Assert.Single(current.Areas).Fee);
        var saved = await SaveAsync(admin, Input(current));
        using var guest = factory.CreateStaffClient();
        Assert.Equal(5m, Assert.Single((await guest.GetFromJsonAsync<PublicDeliveryAreaResponse[]>("/api/public-checkout/delivery-areas"))!).Fee);
        var product = new Product
        {
            Name = "Lanche de teste",
            NormalizedName = "LANCHE DE TESTE",
            Price = 23.90m,
            IsActive = true,
            IsAvailable = true,
            Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" }
        };
        await factory.WithDatabaseAsync(async database =>
        {
            database.Products.Add(product);
            OrderStockFixture.AddRecipe(database, product);
            await database.SaveChangesAsync();
        });
        var delivery = new PublicCheckoutRequest("Cliente teste", "31999991234", new([new(product.Id, 1)]))
        { Fulfillment = "Delivery", Address = new("legacy-center", "Rua de teste", "10") };
        var reviewResponse = await guest.PostAsJsonAsync("/api/public-checkout/review", delivery);
        reviewResponse.EnsureSuccessStatusCode();
        var review = (await reviewResponse.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        Assert.Equal(5m, review.DeliveryFee);
        Assert.Equal(28.90m, review.Total);
        var request = new PublicOrderRequest(Guid.NewGuid(), review.ReviewToken, delivery);
        var creation = await guest.PostAsJsonAsync("/api/public-checkout/orders", request);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var receipt = (await creation.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Configuration["PublicDelivery:FixedFee"] = "9.00";
        var replay = await guest.PostAsJsonAsync("/api/public-checkout/orders", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var recovered = (await replay.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Assert.Equal(receipt.Number, recovered.Number);
        Assert.Equal(28.90m, recovered.Total);
        var pickup = new PublicCheckoutRequest("Cliente teste", "31999991234", delivery.Cart);
        var pickupResponse = await guest.PostAsJsonAsync("/api/public-checkout/review", pickup);
        pickupResponse.EnsureSuccessStatusCode();
        var pickupReview = (await pickupResponse.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        Assert.Equal(0m, pickupReview.DeliveryFee);
        Assert.Equal(23.90m, pickupReview.Total);
        Assert.Null(pickupReview.Address);
        await factory.WithDatabaseAsync(async database => Assert.Equal(5m, (await database.Orders.SingleAsync()).DeliveryFee));
        Assert.Equal(5m, Assert.Single(saved.Areas).Fee);
    }

    [Fact]
    public async Task FixedFeeChangeAfterReviewRequiresNewReviewWithoutPartialOrder()
    {
        Configuration["PublicDelivery:FixedFee"] = "5.00";
        var product = new Product
        {
            Name = "Lanche de teste",
            NormalizedName = "LANCHE DE TESTE",
            Price = 23.90m,
            IsActive = true,
            IsAvailable = true,
            Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" }
        };
        await factory.WithDatabaseAsync(async database =>
        {
            database.Products.Add(product);
            OrderStockFixture.AddRecipe(database, product);
            await database.SaveChangesAsync();
        });
        using var guest = factory.CreateStaffClient();
        var checkout = new PublicCheckoutRequest("Cliente teste", "31999991234", new([new(product.Id, 1)]))
        { Fulfillment = "Delivery", Address = new("legacy-center", "Rua de teste", "10") };
        var reviewed = await guest.PostAsJsonAsync("/api/public-checkout/review", checkout);
        reviewed.EnsureSuccessStatusCode();
        var review = (await reviewed.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        Configuration["PublicDelivery:FixedFee"] = "6.00";
        await AssertConflictAsync(await guest.PostAsJsonAsync("/api/public-checkout/orders",
            new PublicOrderRequest(Guid.NewGuid(), review.ReviewToken, checkout)), "OrderReviewChanged");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.Customers.AnyAsync());
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorsCanReadOrWrite(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var expected = role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await client.GetAsync("/api/delivery-settings")).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync("/api/delivery-settings", new SaveDeliverySettingsRequest(new string('A', 64), []))).StatusCode);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.DeliverySettings.AnyAsync()));
    }

    [Fact]
    public async Task LegacyCoverageIsReadOnlyUntilFirstSaveAndNeverResurrectsPausedAreas()
    {
        using var admin = await SignInAsync();
        using var guest = factory.CreateStaffClient();
        var initial = await ReadAsync(admin);
        Assert.Null(initial.UpdatedAt);
        Assert.Equal(5.50m, Assert.Single(initial.Areas).Fee);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.DeliverySettings.AnyAsync()));
        var request = Input(initial) with { Areas = [Input(initial).Areas[0] with { IsActive = false }] };
        var saved = await SaveAsync(admin, request);
        Assert.NotEqual(initial.Revision, saved.Revision);
        Assert.NotNull(saved.UpdatedAt);
        Assert.Equal("Test staff", saved.UpdatedBy);
        Configuration[AreaKey + "Fee"] = "99.00";
        Assert.Empty((await guest.GetFromJsonAsync<PublicDeliveryAreaResponse[]>("/api/public-checkout/delivery-areas"))!);
        Assert.False(Assert.Single((await ReadAsync(admin)).Areas).IsActive);
        var replay = await SaveAsync(admin, request);
        Assert.Equal(saved.Revision, replay.Revision);
        Assert.Equal(saved.UpdatedAt, replay.UpdatedAt);
        var active = Input(saved) with { Areas = [request.Areas[0] with { IsActive = true, Fee = 0 }] };
        await SaveAsync(admin, active);
        var publicResponse = await guest.GetAsync("/api/public-checkout/delivery-areas");
        Assert.True(publicResponse.Headers.CacheControl?.NoStore);
        Assert.Equal(0m, Assert.Single((await publicResponse.Content.ReadFromJsonAsync<PublicDeliveryAreaResponse[]>())!).Fee);
        Assert.DoesNotContain("UpdatedBy", await publicResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangesToLegacyConfigurationInvalidateOpenEditorAndRemovalIsRejected()
    {
        using var admin = await SignInAsync();
        var initial = await ReadAsync(admin);
        Configuration[AreaKey + "Fee"] = "7.00";
        await AssertConflictAsync(await admin.PutAsJsonAsync("/api/delivery-settings", Input(initial)), "DeliverySettingsChanged");
        var current = await ReadAsync(admin);
        await AssertConflictAsync(await admin.PutAsJsonAsync("/api/delivery-settings", Input(current) with { Areas = [] }), "DeliveryAreaRemovalDenied");
        await factory.WithDatabaseAsync(async database => Assert.False(await database.DeliverySettings.AnyAsync()));
    }

    [Theory]
    [InlineData("precision")]
    [InlineData("negative")]
    [InlineData("missingFee")]
    [InlineData("missingStatus")]
    [InlineData("state")]
    [InlineData("blank")]
    [InlineData("length")]
    [InlineData("duplicateId")]
    [InlineData("duplicatePlace")]
    [InlineData("nullArea")]
    [InlineData("nullList")]
    [InlineData("excess")]
    [InlineData("unknownField")]
    [InlineData("invalidId")]
    public async Task InvalidCoverageDoesNotWrite(string condition)
    {
        using var admin = await SignInAsync();
        var request = JsonSerializer.SerializeToNode(Input(await ReadAsync(admin)), JsonOptions)!;
        var area = request["areas"]![0]!;
        switch (condition)
        {
            case "precision":
                area["fee"] = 1.001m;
                break;
            case "negative":
                area["fee"] = -1;
                break;
            case "missingFee":
                area.AsObject().Remove("fee");
                break;
            case "missingStatus":
                area.AsObject().Remove("isActive");
                break;
            case "state":
                area["state"] = "XX";
                break;
            case "blank":
                area["neighborhood"] = " ";
                break;
            case "length":
                area["city"] = new string('a', 81);
                break;
            case "duplicateId":
                request["areas"]!.AsArray().Add(area.DeepClone());
                break;
            case "duplicatePlace":
                var duplicate = area.DeepClone();
                duplicate["id"] = "other";
                duplicate["neighborhood"] = " centro DE TESTE ";
                request["areas"]!.AsArray().Add(duplicate);
                break;
            case "nullArea":
                request["areas"]![0] = null;
                break;
            case "nullList":
                request["areas"] = null;
                break;
            case "excess":
                request["areas"] = new JsonArray(Enumerable.Range(0, 501).Select(_ => area.DeepClone()).ToArray());
                break;
            case "unknownField":
                request["actorId"] = Guid.NewGuid();
                break;
            case "invalidId":
                area["id"] = "../escape";
                break;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/delivery-settings", request)).StatusCode);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.DeliverySettings.AnyAsync()));
    }

    [Fact]
    public async Task ConcurrentEditsHaveOneWinnerAndLosingChangesAreNotApplied()
    {
        using var admin = await SignInAsync();
        var original = Input(await ReadAsync(admin));
        var requests = Enumerable.Range(1, 6).Select(index => original with
        { Areas = [original.Areas[0] with { Fee = index }] }).ToArray();
        var responses = await Task.WhenAll(requests.Select(request => admin.PutAsJsonAsync("/api/delivery-settings", request)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var winningIndex = Array.FindIndex(responses, response => response.StatusCode == HttpStatusCode.OK);
        var current = await ReadAsync(admin);
        Assert.Equal(winningIndex + 1, current.Areas[0].Fee);
        Assert.Equal(current.Revision, (await SaveAsync(admin, requests[winningIndex])).Revision);
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, (await database.DeliverySettings.SingleAsync()).Version));
    }

    [Fact]
    public async Task FeeChangeRejectsOldReviewAndRecordedOrderRetainsSnapshotAfterPause()
    {
        using var admin = await SignInAsync();
        using var guest = factory.CreateStaffClient();
        var settings = await SaveAsync(admin, Input(await ReadAsync(admin)));
        var product = new Product
        {
            Name = "Teste",
            NormalizedName = "TESTE",
            Price = 20,
            IsActive = true,
            IsAvailable = true,
            Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" }
        };
        await factory.WithDatabaseAsync(async database =>
        {
            database.Products.Add(product);
            OrderStockFixture.AddRecipe(database, product);
            await database.SaveChangesAsync();
        });
        var checkout = new PublicCheckoutRequest("Visitante teste", "31999991234", new([new(product.Id, 1)]))
        { Fulfillment = "Delivery", Address = new("legacy-center", "Rua de teste", "10") };
        var reviewResponse = await guest.PostAsJsonAsync("/api/public-checkout/review", checkout);
        reviewResponse.EnsureSuccessStatusCode();
        var review = (await reviewResponse.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        var request = new PublicOrderRequest(Guid.NewGuid(), review.ReviewToken, checkout);
        settings = await SaveAsync(admin, Input(settings) with { Areas = [Input(settings).Areas[0] with { Fee = 8m }] });
        await AssertConflictAsync(await guest.PostAsJsonAsync("/api/public-checkout/orders", request), "OrderReviewChanged");
        await factory.WithDatabaseAsync(async database => { Assert.False(await database.Orders.AnyAsync()); Assert.False(await database.Customers.AnyAsync()); });
        reviewResponse = await guest.PostAsJsonAsync("/api/public-checkout/review", checkout);
        reviewResponse.EnsureSuccessStatusCode();
        review = (await reviewResponse.Content.ReadFromJsonAsync<PublicCheckoutReviewResponse>())!;
        request = request with { ReviewToken = review.ReviewToken };
        var created = await guest.PostAsJsonAsync("/api/public-checkout/orders", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        await SaveAsync(admin, Input(settings) with { Areas = [Input(settings).Areas[0] with { IsActive = false, Fee = 9 }] });
        var replay = await guest.PostAsJsonAsync("/api/public-checkout/orders", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var recovered = (await replay.Content.ReadFromJsonAsync<PublicOrderReceipt>())!;
        Assert.Equal(receipt.Number, recovered.Number);
        Assert.Equal(receipt.Total, recovered.Total);
        Assert.Equal(receipt.Fulfillment, recovered.Fulfillment);
        Assert.Equal(receipt.CreatedAt, recovered.CreatedAt);
        Assert.NotNull(recovered.Tracking);
        await AssertConflictAsync(await guest.PostAsJsonAsync("/api/public-checkout/review", checkout), "PublicDeliveryUnavailable");
        await factory.WithDatabaseAsync(async database =>
        {
            var order = await database.Orders.SingleAsync();
            Assert.Equal(8m, order.DeliveryFee);
            Assert.Equal(28m, order.Total);
        });
    }

    [Fact]
    public async Task RollbackRefusesToDiscardSavedCoverage()
    {
        using var admin = await SignInAsync();
        await SaveAsync(admin, Input(await ReadAsync(admin)));
        await factory.WithDatabaseAsync(async database =>
        {
            var script = database.GetService<IMigrator>().GenerateScript("20261006174915_AddDeliverySettings", "20261005210534_AddPublicDelivery", MigrationsSqlGenerationOptions.NoTransactions);
            await using var transaction = await database.Database.BeginTransactionAsync();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(script));
            Assert.Contains("Delivery settings exist", failure.MessageText);
            await transaction.RollbackAsync();
            Assert.True(await database.DeliverySettings.AnyAsync());
        });
    }
}
