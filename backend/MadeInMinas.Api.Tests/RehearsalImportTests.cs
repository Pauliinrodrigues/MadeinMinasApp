using MadeInMinas.AiPilot;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using MadeInMinas.Api.DTOs.StoreAttendance;
using MadeInMinas.Api.DTOs.PublicCheckout;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class RehearsalImportTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private static async Task ClearAsync(AppDbContext database)
    {
        await database.ChatConversations.ExecuteDeleteAsync();
        await database.StoreAttendanceSettings.ExecuteDeleteAsync();
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
    public Task InitializeAsync() => factory.WithDatabaseAsync(ClearAsync);
    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    [Fact]
    public async Task ImportedSettingsCanBeReadThroughExistingPublicServices()
    {
        var source = RehearsalCatalogTests.Sample();
        await factory.WithDatabaseAsync(async database =>
        {
            var admin = new User { Name = "Administrador fictício", Username = "piloto.admin", NormalizedUsername = "PILOTO.ADMIN", RoleId = 1 };
            database.Users.Add(admin);
            await database.SaveChangesAsync();
            await RehearsalCatalog.ApplyAsync(database, source, admin);
        });
        using var client = factory.CreateClient();
        var attendance = (await client.GetFromJsonAsync<PublicStoreAttendanceResponse>("/api/store-attendance/public"))!;
        Assert.Equal(source.Attendance.Hours, attendance.Hours);
        Assert.Equal(source.Attendance.PaymentMethods.Order(), attendance.PaymentMethods.Order());
        Assert.Equal(source.Attendance.DeliveryEstimateMessage, attendance.DeliveryEstimateMessage);
        var areas = (await client.GetFromJsonAsync<PublicDeliveryAreaResponse[]>("/api/public-checkout/delivery-areas"))!;
        var actual = Assert.Single(areas);
        Assert.Equal(source.Areas[0].Id, actual.Id);
        Assert.Equal(source.Areas[0].City, actual.City);
        Assert.Equal(source.Areas[0].Fee, actual.Fee);
        Assert.Equal(source.Areas[0].CoversAllNeighborhoods, actual.CoversAllNeighborhoods);
    }

    [Fact]
    public Task ReimportUpdatesCatalogWithoutDuplicatingRecipesResettingStockOrChangingHistory() => factory.WithDatabaseAsync(async database =>
    {
        var admin = new User { Name = "Administrador fictício", Username = "piloto.admin", NormalizedUsername = "PILOTO.ADMIN", RoleId = 1 };
        database.Users.Add(admin);
        await database.SaveChangesAsync();
        var source = RehearsalCatalogTests.Sample();
        await RehearsalCatalog.ApplyAsync(database, source, admin);
        var ingredient = await database.Ingredients.SingleAsync();
        ingredient.CurrentStock = 998m;
        var customer = new Customer { Name = "Cliente fictício", NormalizedName = "CLIENTE FICTÍCIO", Phone = "+5538999991357" };
        database.Customers.Add(customer);
        var historical = new Order
        {
            Number = 1,
            RequestId = Guid.NewGuid(),
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerPhone = customer.Phone,
            Fulfillment = "Pickup",
            Origin = "DirectLink",
            Subtotal = 20m,
            Total = 20m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = [new OrderItem { Position = 1, ProductId = source.Products[0].Id, ProductName = source.Products[0].Name, Quantity = 1, UnitPrice = 20m, LineTotal = 20m }]
        };
        database.Orders.Add(historical);
        await database.SaveChangesAsync();
        await RehearsalCatalog.ApplyAsync(database, source with { Products = [source.Products[0] with { Price = 25m }] }, admin);
        database.ChangeTracker.Clear();
        Assert.Equal(1, await database.Products.CountAsync());
        Assert.Equal(1, await database.Categories.CountAsync());
        Assert.Equal(1, await database.Recipes.CountAsync());
        Assert.Equal(998m, (await database.Ingredients.SingleAsync()).CurrentStock);
        Assert.Equal(25m, (await database.Products.SingleAsync()).Price);
        var saved = await database.Orders.Include(order => order.Items).SingleAsync();
        Assert.Equal(20m, saved.Total);
        Assert.Equal(20m, Assert.Single(saved.Items).UnitPrice);
        Assert.Equal(historical.Id, saved.Id);
    });
}
