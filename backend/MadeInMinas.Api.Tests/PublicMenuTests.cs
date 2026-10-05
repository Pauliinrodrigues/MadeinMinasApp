using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Menu;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class PublicMenuTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Category burgers = new() { Name = "Lanches", NormalizedName = "LANCHES", DisplayOrder = 10 };
    private readonly Category drinks = new() { Name = "Bebidas", NormalizedName = "BEBIDAS", DisplayOrder = 0 };
    private readonly Category hidden = new() { Name = "Interna", NormalizedName = "INTERNA", IsActive = false };
    private Product burger = null!;
    private Product paused = null!;
    private Product drink = null!;

    private static async Task ClearAsync(AppDbContext database)
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    }

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await ClearAsync(database);
        burger = Product("Uai Sô", burgers, 29.90m);
        burger.Description = "Pão, blend e queijo. <b>Texto</b>";
        burger.ImageUrl = "https://images.example.test/burger.jpg";
        paused = Product("Especial", burgers, 35.50m);
        paused.IsAvailable = false;
        drink = Product("Guaraná", drinks, 5.75m);
        var inactive = Product("Rascunho", burgers, 10m);
        inactive.IsActive = false;
        database.Products.AddRange(burger, paused, drink, inactive, Product("Interno", hidden, 20m));
        database.Categories.Add(new Category { Name = "Vazia", NormalizedName = "VAZIA" });
        var ingredient = new Ingredient { Name = "Custo privado", NormalizedName = "CUSTO PRIVADO", Unit = "un", UnitCost = 11.1234m, CurrentStock = 19m };
        database.Recipes.Add(new Recipe { Product = burger, YieldQuantity = 1, Instructions = "Preparo privado", Items = [new RecipeItem { Ingredient = ingredient, Quantity = 1 }] });
        await database.SaveChangesAsync();
    });

    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    private static Product Product(string name, Category category, decimal price) => new()
    {
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Category = category,
        Price = price,
        IsActive = true,
        IsAvailable = true
    };

    private static async Task<PublicMenuResponse> ReadAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/menu" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<PublicMenuResponse>())!;
    }

    [Fact]
    public async Task AnonymousMenuContainsOnlyActiveCatalogInDisplayOrder()
    {
        using var client = factory.CreateStaffClient();
        var result = await ReadAsync(client);
        Assert.Equal([drinks.Id, burgers.Id], result.Categories.Select(item => item.Id));
        Assert.Equal([drink.Id, paused.Id, burger.Id], result.Items.Select(item => item.Id));
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(24, result.PageSize);
        Assert.False(result.Items.Single(item => item.Id == paused.Id).IsAvailable);
        Assert.Equal(29.90m, result.Items.Single(item => item.Id == burger.Id).Price);
    }

    [Fact]
    public async Task PublicContractDoesNotExposeRecipeCostsStockOrAdministrativeFields()
    {
        using var client = factory.CreateStaffClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/menu"));
        Assert.Equal(["categories", "items", "page", "pageSize", "totalCount"], json.RootElement.EnumerateObject().Select(item => item.Name).Order());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            Assert.Equal(["categoryId", "description", "id", "imageUrl", "isAvailable", "name", "price"], item.EnumerateObject().Select(field => field.Name).Order());
        foreach (var category in json.RootElement.GetProperty("categories").EnumerateArray())
            Assert.Equal(["id", "name"], category.EnumerateObject().Select(field => field.Name).Order());
        Assert.DoesNotContain("privado", json.RootElement.GetRawText());
    }

    [Fact]
    public async Task CategoryFilterAndPaginationKeepAllNavigationCategories()
    {
        using var client = factory.CreateStaffClient();
        var first = await ReadAsync(client, $"?categoryId={burgers.Id}&pageSize=1");
        var second = await ReadAsync(client, $"?categoryId={burgers.Id}&pageSize=1&page=2");
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(paused.Id, Assert.Single(first.Items).Id);
        Assert.Equal(burger.Id, Assert.Single(second.Items).Id);
        Assert.Equal(2, second.Page);
        Assert.Equal(first.Categories, second.Categories);
        Assert.Empty((await ReadAsync(client, $"?categoryId={burgers.Id}&page=1000000")).Items);
    }

    [Fact]
    public async Task HiddenAndUnknownCategoriesHaveTheSameEmptyResult()
    {
        using var client = factory.CreateStaffClient();
        var hiddenResult = await ReadAsync(client, $"?categoryId={hidden.Id}");
        var missingResult = await ReadAsync(client, $"?categoryId={Guid.NewGuid()}");
        Assert.Empty(hiddenResult.Items);
        Assert.Equal(0, hiddenResult.TotalCount);
        Assert.Equal(hiddenResult.TotalCount, missingResult.TotalCount);
        Assert.Equal(hiddenResult.Categories, missingResult.Categories);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=1000001")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=49")]
    [InlineData("?categoryId=invalid")]
    public async Task InvalidQueriesReturnValidationProblem(string query)
    {
        using var client = factory.CreateStaffClient();
        var response = await client.GetAsync("/api/menu" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task MenuReflectsCurrentPricesAndAvailabilityWithoutWriting()
    {
        using var client = factory.CreateStaffClient();
        await ReadAsync(client);
        await factory.WithDatabaseAsync(async database =>
        {
            var product = await database.Products.FindAsync(burger.Id);
            product!.Price = 31.25m;
            product.IsAvailable = false;
            await database.SaveChangesAsync();
        });
        var result = await ReadAsync(client);
        Assert.Equal(31.25m, result.Items.Single(item => item.Id == burger.Id).Price);
        Assert.False(result.Items.Single(item => item.Id == burger.Id).IsAvailable);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(19m, (await database.Ingredients.SingleAsync()).CurrentStock);
            Assert.Equal(11.1234m, (await database.Ingredients.SingleAsync()).UnitCost);
            Assert.Equal(1, await database.Recipes.CountAsync());
            Assert.Equal(5, await database.Products.CountAsync());
            Assert.False(await database.Orders.AnyAsync());
            Assert.False(await database.StockMovements.AnyAsync());
        });
    }

    [Fact]
    public async Task CategoryDeactivationRemovesProductsOnNextRead()
    {
        using var client = factory.CreateStaffClient();
        await ReadAsync(client);
        await factory.WithDatabaseAsync(async database =>
            await database.Categories.Where(item => item.Id == burgers.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.IsActive, false)));
        var result = await ReadAsync(client);
        Assert.Equal(drink.Id, Assert.Single(result.Items).Id);
        Assert.Equal(drinks.Id, Assert.Single(result.Categories).Id);
    }

    [Fact]
    public async Task EmptyCatalogReturnsAnEmptyPage()
    {
        await factory.WithDatabaseAsync(async database =>
            await database.Products.ExecuteUpdateAsync(update => update.SetProperty(item => item.IsActive, false)));
        using var client = factory.CreateStaffClient();
        var result = await ReadAsync(client);
        Assert.Empty(result.Categories);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task InvalidStaffTokenDoesNotPreventPublicReadingOrUnlockAdministration()
    {
        using var client = factory.CreateStaffClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        await ReadAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/products/{burger.Id}/costing")).StatusCode);
    }
}
