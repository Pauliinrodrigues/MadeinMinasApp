using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Costing;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class ProductCostTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Ingredient meat = new() { Name = "Carne", NormalizedName = "CARNE", Unit = "kg", UnitCost = 35.1234m, CurrentStock = 7m };
    private readonly Ingredient bread = new() { Name = "Pão", NormalizedName = "PÃO", Unit = "un", UnitCost = 1.25m };
    private readonly Ingredient packaging = new() { Name = "Embalagem", NormalizedName = "EMBALAGEM", Unit = "un", UnitCost = 0.35m };
    private Product product = null!;
    private string Path => $"/api/products/{product.Id}/costing";

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
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
        database.Ingredients.AddRange(meat, bread, packaging);
        database.Recipes.Add(new Recipe
        {
            ProductId = product.Id,
            YieldQuantity = 2,
            Items = [
            new RecipeItem { IngredientId = meat.Id, Quantity = 0.300m },
            new RecipeItem { IngredientId = bread.Id, Quantity = 2m, Position = 1 },
            new RecipeItem { IngredientId = packaging.Id, Quantity = 2m, Position = 2 }]
        });
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    private async Task<ProductCostResponse> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<ProductCostResponse>())!;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CostsAreRestrictedToAdministrator(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var response = await client.GetAsync(Path);
        Assert.Equal(role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("35.1234", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RecipeIncludingPackagingUsesYieldAndOriginalPrecisionWithoutWriting()
    {
        using var client = await SignInAsync();
        var result = await ReadAsync(client);
        Assert.Equal("Ready", result.Status);
        Assert.Equal(2, result.RecipeYield);
        Assert.Equal(13.73702m, result.RecipeCost);
        Assert.Equal(13.73702m, result.KnownRecipeCost);
        Assert.Equal(6.86851m, result.UnitCost);
        Assert.Equal(22.97m, result.CmvPercentage);
        Assert.Equal(23.03149m, result.GrossMargin);
        Assert.Equal(77.03m, result.GrossMarginPercentage);
        Assert.Equal(new[] { "Carne", "Pão", "Embalagem" }, result.Items.Select(item => item.IngredientName));
        Assert.Equal(10.53702m, result.Items[0].RecipeCost);
        Assert.True(result.IsAvailableForSale);
        Assert.False(result.HasInactiveIngredients);
        Assert.InRange(result.CalculatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        await ReadAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            var stored = await database.Ingredients.SingleAsync(item => item.Id == meat.Id);
            Assert.Equal(7m, stored.CurrentStock);
            Assert.Equal(0, stored.StockVersion);
            Assert.Equal(meat.UpdatedAt, stored.UpdatedAt);
            Assert.Equal(29.90m, (await database.Products.SingleAsync()).Price);
            Assert.Empty(await database.StockMovements.ToListAsync());
            Assert.Empty(await database.Orders.ToListAsync());
            Assert.Empty(await database.Payments.ToListAsync());
        });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync(Path, new { salePrice = 1 })).StatusCode);
    }

    [Fact]
    public async Task MissingRecipeDoesNotInventZeroCostOrMarginAndMissingProductIs404()
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database => await database.Recipes.ExecuteDeleteAsync());
        var result = await ReadAsync(client);
        Assert.Equal("MissingRecipe", result.Status);
        Assert.Null(result.RecipeYield);
        Assert.Null(result.UnitCost);
        Assert.Null(result.CmvPercentage);
        Assert.Null(result.GrossMargin);
        Assert.Null(result.GrossMarginPercentage);
        Assert.Empty(result.Items);
        var missing = await client.GetAsync($"/api/products/{Guid.NewGuid()}/costing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var json = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal("ProductNotFound", json.RootElement.GetProperty("code").GetString());
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.Recipes.ToListAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroCostsAreIncompleteAndNeverShowAnArtificialMargin(bool allZero)
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database => await database.Ingredients.Where(item => allZero || item.Id == meat.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.UnitCost, 0m)));
        var result = await ReadAsync(client);
        Assert.Equal("MissingCosts", result.Status);
        Assert.Equal(allZero ? 0m : 3.2m, result.KnownRecipeCost);
        Assert.Null(result.RecipeCost);
        Assert.Null(result.UnitCost);
        Assert.Null(result.CmvPercentage);
        Assert.Null(result.GrossMargin);
        Assert.Null(result.GrossMarginPercentage);
        Assert.Equal(3, result.Items.Length);
    }

    [Fact]
    public async Task InactiveProductAndIngredientsStillCalculateWithExplicitFlags()
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            await database.Ingredients.Where(item => item.Id == meat.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        });
        var result = await ReadAsync(client);
        Assert.Equal("Ready", result.Status);
        Assert.Equal(6.86851m, result.UnitCost);
        Assert.False(result.ProductIsActive);
        Assert.False(result.IsAvailableForSale);
        Assert.True(result.HasInactiveIngredients);
        Assert.False(result.Items[0].IngredientIsActive);
    }

    [Fact]
    public async Task RefreshUsesChangedCostsPriceYieldAndComposition()
    {
        using var client = await SignInAsync();
        var original = await ReadAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Ingredients.Where(item => item.Id == meat.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.UnitCost, 40m));
            await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 20m));
            await database.Categories.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            await database.Recipes.ExecuteUpdateAsync(set => set.SetProperty(item => item.YieldQuantity, 4));
            await database.RecipeItems.Where(item => item.IngredientId == packaging.Id).ExecuteDeleteAsync();
        });
        var updated = await ReadAsync(client);
        Assert.Equal(14.5m, updated.RecipeCost);
        Assert.Equal(3.625m, updated.UnitCost);
        Assert.Equal(18.13m, updated.CmvPercentage);
        Assert.Equal(81.87m, updated.GrossMarginPercentage);
        Assert.Equal(16.375m, updated.GrossMargin);
        Assert.Equal(2, updated.Items.Length);
        Assert.False(updated.IsAvailableForSale);
        Assert.Equal(6.86851m, original.UnitCost);
    }

    [Fact]
    public async Task CostAboveSalePriceReturnsNegativeMarginWithoutClamping()
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database => await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 1m)));
        var result = await ReadAsync(client);
        Assert.Equal(686.85m, result.CmvPercentage);
        Assert.Equal(-5.86851m, result.GrossMargin);
        Assert.Equal(-586.85m, result.GrossMarginPercentage);
    }

    [Theory]
    [InlineData("0.0049", 3, "0.0000049")]
    [InlineData("0.0001", 10000, "0.0000001")]
    public async Task SmallAmountsDoNotUseStockRoundingOrPrematureMoneyRounding(string costText, int yield, string batchText)
    {
        using var client = await SignInAsync();
        var cost = decimal.Parse(costText, System.Globalization.CultureInfo.InvariantCulture);
        var batch = decimal.Parse(batchText, System.Globalization.CultureInfo.InvariantCulture);
        await factory.WithDatabaseAsync(async database =>
        {
            await database.RecipeItems.Where(item => item.IngredientId != meat.Id).ExecuteDeleteAsync();
            await database.RecipeItems.ExecuteUpdateAsync(set => set.SetProperty(item => item.Quantity, 0.001m));
            await database.Ingredients.Where(item => item.Id == meat.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.UnitCost, cost));
            await database.Recipes.ExecuteUpdateAsync(set => set.SetProperty(item => item.YieldQuantity, yield));
        });
        var result = await ReadAsync(client);
        Assert.Equal("Ready", result.Status);
        Assert.Equal(batch, result.RecipeCost);
        Assert.Equal(batch / yield, result.UnitCost);
        Assert.True(result.UnitCost > 0);
    }

    [Fact]
    public async Task MaximumSupportedRecipeAndMinimumPriceDoNotOverflow()
    {
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            var recipe = await database.Recipes.Include(item => item.Items).SingleAsync();
            recipe.YieldQuantity = 1;
            database.RecipeItems.RemoveRange(recipe.Items);
            recipe.Items.Clear();
            for (var index = 0; index < 100; index++)
            {
                var ingredient = new Ingredient { Name = "Máximo " + index, NormalizedName = "MÁXIMO " + index, Unit = "kg", UnitCost = 999999.9999m };
                database.Ingredients.Add(ingredient);
                recipe.Items.Add(new RecipeItem { IngredientId = ingredient.Id, Quantity = 999999.999m, Position = index });
            }
            await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.Price, 0.01m));
            await database.SaveChangesAsync();
        });
        var result = await ReadAsync(client);
        Assert.Equal(999999.999m * 999999.9999m * 100m, result.RecipeCost);
        Assert.Equal(result.RecipeCost, result.UnitCost);
        Assert.Equal(100m, result.CmvPercentage + result.GrossMarginPercentage);
        Assert.Equal(100, result.Items.Length);
    }
}
