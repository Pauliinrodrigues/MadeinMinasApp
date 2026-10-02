using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.Kitchen;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.DTOs.Stock;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class OrderStockTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Customer customer = new() { Name = "Maria", NormalizedName = "MARIA", Phone = "+5531999991234" };
    private readonly Ingredient meat = new() { Name = "Carne", NormalizedName = "CARNE", Unit = "kg", CurrentStock = 10m, UnitCost = 35m };
    private readonly Ingredient bread = new() { Name = "Pão", NormalizedName = "PÃO", Unit = "un", CurrentStock = 10m };
    private Product burger = null!;

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
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
        database.Customers.Add(customer);
        database.Ingredients.AddRange(meat, bread);
        database.Recipes.Add(new Recipe
        {
            ProductId = burger.Id,
            YieldQuantity = 3,
            Items = [new RecipeItem { IngredientId = meat.Id, Quantity = 0.5m }, new RecipeItem { IngredientId = bread.Id, Quantity = 3m, Position = 1 }]
        });
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
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    private async Task<OrderResponse> CreateAsync(HttpClient client, CartItemRequest[]? items = null)
    {
        var cart = new CartQuoteRequest(customer.Id, "Pickup", items ?? [new(burger.Id, 2)]);
        var quote = await client.PostAsJsonAsync("/api/cart/quote", cart);
        quote.EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(Guid.NewGuid(),
            (await quote.Content.ReadFromJsonAsync<CartQuoteResponse>())!.ReviewToken, cart));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static string Path(OrderResponse order) => $"/api/orders/{order.Id}/status";
    private static async Task<OrderResponse> ChangeAsync(HttpClient client, OrderResponse order, string status = "Confirmed")
    {
        var response = await client.PutAsJsonAsync(Path(order), new OrderStatusRequest(status, order.Version, status == "Cancelled" ? "Cliente desistiu" : null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmationAggregatesProductLinesRoundsOnceAndWritesAtomicAuditForAttendant()
    {
        using var client = await SignInAsync(2);
        var order = await CreateAsync(client, [new(burger.Id, 1, "Sem cebola"), new(burger.Id, 1, "Normal")]);
        Assert.Equal("Pending", order.StockStatus);
        Assert.Empty(order.StockComponents);
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.StockMovements.ToListAsync()));
        var confirmed = await ChangeAsync(client, order);
        Assert.Equal("Consumed", confirmed.StockStatus);
        Assert.Equal(2, confirmed.StockComponents.Length);
        var component = Assert.Single(confirmed.StockComponents, item => item.IngredientId == meat.Id);
        Assert.Equal(0.334m, component.ConsumedQuantity);
        Assert.Equal(0.5m, component.RecipeQuantity);
        Assert.Equal(3, component.RecipeYield);
        Assert.Equal(2, component.ProductQuantity);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(9.666m, (await database.Ingredients.SingleAsync(item => item.Id == meat.Id)).CurrentStock);
            Assert.Equal(8m, (await database.Ingredients.SingleAsync(item => item.Id == bread.Id)).CurrentStock);
            var moves = await database.StockMovements.ToArrayAsync();
            Assert.Equal(2, moves.Length);
            Assert.All(moves, move => { Assert.Equal(order.Id, move.OrderId); Assert.Equal("Exit", move.Type); Assert.Equal(confirmed.UpdatedAt, move.CreatedAt); Assert.Equal("Test staff", move.ActorName); });
            Assert.Equal(35m, (await database.Ingredients.SingleAsync(item => item.Id == meat.Id)).UnitCost);
        });
        var read = (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        Assert.Equal(confirmed.StockComponents, read.StockComponents);
    }

    [Theory]
    [InlineData("missing", "OrderRecipeRequired")]
    [InlineData("inactive", "OrderIngredientInactive")]
    [InlineData("insufficient", "OrderInsufficientStock")]
    [InlineData("overflow", "OrderStockQuantityExceeded")]
    public async Task InvalidCompositionOrBalanceLeavesWholeOrderAndStockUntouched(string condition, string code)
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            if (condition == "missing")
                await database.Recipes.ExecuteDeleteAsync();
            if (condition == "inactive")
                await database.Ingredients.Where(item => item.Id == bread.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            if (condition == "insufficient")
                await database.Ingredients.Where(item => item.Id == bread.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.CurrentStock, 0m));
            if (condition == "overflow")
            {
                await database.Recipes.ExecuteUpdateAsync(set => set.SetProperty(item => item.YieldQuantity, 1));
                await database.RecipeItems.ExecuteUpdateAsync(set => set.SetProperty(item => item.Quantity, 999999.999m));
            }
        });
        await ProblemAsync(await client.PutAsJsonAsync(Path(order), new OrderStatusRequest("Confirmed", 1)), code);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Empty(await database.StockMovements.ToListAsync());
            Assert.Empty(await database.OrderStockComponents.ToListAsync());
            Assert.Equal(10m, (await database.Ingredients.SingleAsync(item => item.Id == meat.Id)).CurrentStock);
            var saved = await database.Orders.Include(item => item.History).SingleAsync();
            Assert.Equal("New", saved.Status);
            Assert.Equal("Pending", saved.StockStatus);
            Assert.Single(saved.History);
        });
    }

    [Fact]
    public async Task ConfirmationAndCancellationRetriesNeverDuplicateMovementsAndRestoreOriginalQuantities()
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client);
        var responses = await Task.WhenAll(client.PutAsJsonAsync(Path(order), new OrderStatusRequest("Confirmed", 1)),
            client.PutAsJsonAsync(Path(order), new OrderStatusRequest("Confirmed", 1)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var confirmed = (await responses[0].Content.ReadFromJsonAsync<OrderResponse>())!;
        await factory.WithDatabaseAsync(async database =>
        {
            await database.RecipeItems.ExecuteUpdateAsync(set => set.SetProperty(item => item.Quantity, 99m));
            await database.Ingredients.Where(item => item.Id == meat.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false).SetProperty(item => item.Name, "Carne nova"));
        });
        var cancel = new OrderStatusRequest("Cancelled", 2, "Cliente desistiu");
        var cancelledResponses = await Task.WhenAll(client.PutAsJsonAsync(Path(order), cancel), client.PutAsJsonAsync(Path(order), cancel));
        Assert.All(cancelledResponses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var cancelled = (await cancelledResponses[0].Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal("Returned", cancelled.StockStatus);
        Assert.Equal(confirmed.StockComponents, cancelled.StockComponents);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.All(await database.Ingredients.ToListAsync(), item => { Assert.Equal(10m, item.CurrentStock); Assert.Equal(2, item.StockVersion); });
            Assert.Equal(4, await database.StockMovements.CountAsync());
            Assert.Equal(0m, await database.StockMovements.SumAsync(item => item.Delta));
        });
        using var stockClient = await SignInAsync();
        var stock = (await stockClient.GetFromJsonAsync<StockResponse>($"/api/ingredients/{meat.Id}/stock"))!;
        Assert.All(stock.Movements, move => Assert.Equal(order.Id, move.OrderId));
    }

    [Fact]
    public async Task TwoOrdersCompeteForLastStockWithoutOverdrawing()
    {
        using var client = await SignInAsync();
        var first = await CreateAsync(client);
        var second = await CreateAsync(client);
        await factory.WithDatabaseAsync(async database => await database.Ingredients.Where(item => item.Id == bread.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.CurrentStock, 2m)));
        var responses = await Task.WhenAll(client.PutAsJsonAsync(Path(first), new OrderStatusRequest("Confirmed", 1)),
            client.PutAsJsonAsync(Path(second), new OrderStatusRequest("Confirmed", 1)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        await ProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), "OrderInsufficientStock");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(2, await database.StockMovements.CountAsync());
            Assert.Equal(0m, (await database.Ingredients.SingleAsync(item => item.Id == bread.Id)).CurrentStock);
            Assert.Equal(1, await database.Orders.CountAsync(item => item.Status == "New"));
        });
    }

    [Fact]
    public async Task ManualExitAndConfirmationShareTheIngredientLock()
    {
        using var client = await SignInAsync();
        var order = await CreateAsync(client);
        await factory.WithDatabaseAsync(async database => await database.Ingredients.Where(item => item.Id == bread.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.CurrentStock, 2m)));
        var responses = await Task.WhenAll(client.PutAsJsonAsync(Path(order), new OrderStatusRequest("Confirmed", 1)),
            client.PostAsJsonAsync($"/api/ingredients/{bread.Id}/stock/movements", new StockMovementRequest(Guid.NewGuid(), 0, "Exit", 2, "Saída manual")));
        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await factory.WithDatabaseAsync(async database => Assert.Equal(0m, (await database.Ingredients.SingleAsync(item => item.Id == bread.Id)).CurrentStock));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterPreparationRetainsConsumption(bool ready)
    {
        using var client = await SignInAsync();
        var order = await ChangeAsync(client, await CreateAsync(client));
        (await client.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("InPreparation", 2))).EnsureSuccessStatusCode();
        if (ready)
            (await client.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("Ready", 3))).EnsureSuccessStatusCode();
        order = (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        var cancelled = await ChangeAsync(client, order, "Cancelled");
        Assert.Equal("Retained", cancelled.StockStatus);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(2, await database.StockMovements.CountAsync());
            Assert.Equal(8m, (await database.Ingredients.SingleAsync(item => item.Id == bread.Id)).CurrentStock);
        });
    }

    [Fact]
    public async Task CancellationRacingKitchenCannotReturnIngredientsAfterPreparation()
    {
        using var client = await SignInAsync();
        var order = await ChangeAsync(client, await CreateAsync(client));
        var responses = await Task.WhenAll(client.PutAsJsonAsync(Path(order), new OrderStatusRequest("Cancelled", 2, "Desistência")),
            client.PutAsJsonAsync($"/api/kitchen/orders/{order.Id}/status", new KitchenStatusRequest("InPreparation", 2)));
        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var current = (await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        await factory.WithDatabaseAsync(async database =>
        {
            var balance = (await database.Ingredients.SingleAsync(item => item.Id == bread.Id)).CurrentStock;
            Assert.Equal(current.Status == "Cancelled" ? 10m : 8m, balance);
            Assert.Equal(current.Status == "Cancelled" ? "Returned" : "Consumed", current.StockStatus);
        });
    }

    [Fact]
    public async Task ReturnOverflowRollsBackStatusAndAllIngredients()
    {
        using var client = await SignInAsync();
        var order = await ChangeAsync(client, await CreateAsync(client));
        await factory.WithDatabaseAsync(async database => await database.Ingredients.Where(item => item.Id == bread.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.CurrentStock, 999999.999m)));
        await ProblemAsync(await client.PutAsJsonAsync(Path(order), new OrderStatusRequest("Cancelled", 2, "Desistência")), "OrderStockReturnOverflow");
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal("Confirmed", (await database.Orders.SingleAsync()).Status);
            Assert.Equal(2, await database.StockMovements.CountAsync());
            Assert.Equal(9.666m, (await database.Ingredients.SingleAsync(item => item.Id == meat.Id)).CurrentStock);
        });
    }

    [Fact]
    public async Task NewCancellationNeedsNoRecipeOrStock()
    {
        using var client = await SignInAsync(2);
        var order = await CreateAsync(client);
        await factory.WithDatabaseAsync(async database => await database.Recipes.ExecuteDeleteAsync());
        Assert.Equal("NotRequired", (await ChangeAsync(client, order, "Cancelled")).StockStatus);
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task MigrationMarksOldOrdersWithoutMovingStockAndLeavesNewOrdersEligible()
    {
        using var client = await SignInAsync();
        var old = await CreateAsync(client);
        var pending = await CreateAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Orders.Where(item => item.Id == old.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, "Confirmed"));
            var migrator = database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261002130808_AddIngredientStock");
            try
            { await migrator.MigrateAsync(); }
            finally { await migrator.MigrateAsync(); }
            Assert.Equal("Legacy", (await database.Orders.SingleAsync(item => item.Id == old.Id)).StockStatus);
            Assert.Equal("Pending", (await database.Orders.SingleAsync(item => item.Id == pending.Id)).StockStatus);
            Assert.Empty(await database.StockMovements.ToListAsync());
            Assert.All(await database.Ingredients.ToListAsync(), item => Assert.Equal(10m, item.CurrentStock));
        });
        var cancelled = await ChangeAsync(client, old, "Cancelled");
        Assert.Equal("Legacy", cancelled.StockStatus);
        Assert.Equal("Consumed", (await ChangeAsync(client, pending)).StockStatus);
    }
}
