using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Categories;
using MadeInMinas.Api.DTOs.Ingredients;
using MadeInMinas.Api.DTOs.Products;
using MadeInMinas.Api.DTOs.Recipes;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class RecipeTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });
    public Task DisposeAsync() => Task.CompletedTask;
    private static string Path(Guid productId) => $"/api/products/{productId}/recipe";
    private static RecipeRequest Input(Guid ingredientId, decimal quantity = 0.150m, int yield = 1) => new(yield, [new(ingredientId, quantity)], "  Grelhar e montar  ");
    private async Task<HttpClient> SignInAsync(int roleId = 1)
    {
        var user = await factory.CreateUserAsync(roleId);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }
    private static async Task<ProductResponse> ProductAsync(HttpClient client)
    {
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(Guid.NewGuid().ToString()));
        categoryResponse.EnsureSuccessStatusCode();
        var category = (await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>())!;
        var response = await client.PostAsJsonAsync("/api/products", new ProductRequest("Uai Sô", category.Id, 29.90m, true, true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }
    private static async Task<IngredientResponse> IngredientAsync(HttpClient client, string unit = "kg", bool active = true)
    {
        var response = await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest(Guid.NewGuid().ToString(), unit, 30m, 0m, active));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IngredientResponse>())!;
    }
    private static async Task<RecipeResponse> SaveAsync(HttpClient client, Guid productId, RecipeRequest input, bool created = true)
    {
        var response = await client.PutAsJsonAsync(Path(productId), input);
        Assert.Equal(created ? HttpStatusCode.Created : HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        if (created)
            Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<RecipeResponse>())!;
    }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
    private static void SameRecipe(RecipeResponse expected, RecipeResponse actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.ProductId, actual.ProductId);
        Assert.Equal(expected.YieldQuantity, actual.YieldQuantity);
        Assert.Equal(expected.Instructions, actual.Instructions);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.HasInactiveIngredients, actual.HasInactiveIngredients);
        Assert.Equal(expected.Items, actual.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task BothOperationsRequireAdministrator(int roleId)
    {
        using var client = roleId == 0 ? factory.CreateStaffClient() : await SignInAsync(roleId);
        var expected = roleId == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await client.GetAsync(Path(Guid.NewGuid()))).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync(Path(Guid.NewGuid()), Input(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task MissingRecipeAndMissingProductAreDistinctAndReadDoesNotCreate()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        await ProblemAsync(await client.GetAsync(Path(product.Id)), HttpStatusCode.NotFound, "RecipeNotFound");
        await ProblemAsync(await client.GetAsync(Path(Guid.NewGuid())), HttpStatusCode.NotFound, "ProductNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(Path(Guid.NewGuid()), Input(Guid.NewGuid())), HttpStatusCode.NotFound, "ProductNotFound");
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.Recipes.ToListAsync()));
    }

    [Fact]
    public async Task CreateAndReplacePreserveIdentityAndUnitsAndDoNotChangeProduct()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        var meat = await IngredientAsync(client);
        var bread = await IngredientAsync(client, "un");
        var sauce = await IngredientAsync(client, "l");
        var created = await SaveAsync(client, product.Id, new RecipeRequest(2, [new(meat.Id, 0.300m), new(bread.Id, 2)], "  Montar  "));
        Assert.Equal("Montar", created.Instructions);
        Assert.False(created.HasInactiveIngredients);
        Assert.Equal(new[] { "kg", "un" }, created.Items.Select(item => item.Unit));
        SameRecipe(created, (await client.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!);
        var edited = await SaveAsync(client, product.Id, new RecipeRequest(3, [new(sauce.Id, 0.090m), new(meat.Id, 0.450m)], " "), false);
        Assert.Equal(created.Id, edited.Id);
        Assert.Equal(created.CreatedAt, edited.CreatedAt);
        Assert.True(edited.UpdatedAt >= created.UpdatedAt);
        Assert.Null(edited.Instructions);
        Assert.Equal(new[] { sauce.Id, meat.Id }, edited.Items.Select(item => item.IngredientId));
        Assert.Equal(new[] { 0.090m, 0.450m }, edited.Items.Select(item => item.Quantity));
        SameRecipe(edited, (await client.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!);
        Assert.Equal(product, await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"));
        Assert.Equal(meat, await client.GetFromJsonAsync<IngredientResponse>($"/api/ingredients/{meat.Id}"));
        await factory.WithDatabaseAsync(async database => Assert.Equal(2, await database.RecipeItems.CountAsync()));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Path(product.Id))).StatusCode);
    }

    [Fact]
    public async Task InactiveProductCanHaveRecipeAndNoRecipeDoesNotBlockSale()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        Assert.True(product.IsAvailableForSale);
        (await client.PutAsJsonAsync($"/api/products/{product.Id}/status", new ProductStatusRequest(false))).EnsureSuccessStatusCode();
        var ingredient = await IngredientAsync(client);
        await SaveAsync(client, product.Id, Input(ingredient.Id));
        Assert.False((await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!.IsActive);
    }

    [Fact]
    public async Task InactiveIngredientCannotBeNewlyLinkedButExistingLinkCanBeMaintainedOrRemoved()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        var first = await IngredientAsync(client);
        var second = await IngredientAsync(client);
        await SaveAsync(client, product.Id, Input(first.Id));
        (await client.PutAsJsonAsync($"/api/ingredients/{first.Id}/status", new IngredientStatusRequest(false))).EnsureSuccessStatusCode();
        Assert.True((await client.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!.HasInactiveIngredients);
        var kept = await SaveAsync(client, product.Id, Input(first.Id, 0.200m), false);
        Assert.False(kept.Items[0].IngredientIsActive);
        var otherProduct = await ProductAsync(client);
        await ProblemAsync(await client.PutAsJsonAsync(Path(otherProduct.Id), Input(first.Id)), HttpStatusCode.BadRequest, "InactiveRecipeIngredient");
        var removed = await SaveAsync(client, product.Id, Input(second.Id), false);
        Assert.False(removed.HasInactiveIngredients);
        await ProblemAsync(await client.PutAsJsonAsync(Path(product.Id), Input(first.Id)), HttpStatusCode.BadRequest, "InactiveRecipeIngredient");
        SameRecipe(removed, (await client.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!);
    }

    [Fact]
    public async Task InvalidIngredientRollsBackWholeReplacementAndNewRecipe()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        var ingredient = await IngredientAsync(client);
        var original = await SaveAsync(client, product.Id, Input(ingredient.Id));
        var invalid = new RecipeRequest(4, [new(ingredient.Id, 0.999m), new(Guid.NewGuid(), 1)], "Não gravar");
        await ProblemAsync(await client.PutAsJsonAsync(Path(product.Id), invalid), HttpStatusCode.BadRequest, "InvalidRecipeIngredient");
        SameRecipe(original, (await client.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!);
        var otherProduct = await ProductAsync(client);
        await ProblemAsync(await client.PutAsJsonAsync(Path(otherProduct.Id), invalid), HttpStatusCode.BadRequest, "InvalidRecipeIngredient");
        await ProblemAsync(await client.GetAsync(Path(otherProduct.Id)), HttpStatusCode.NotFound, "RecipeNotFound");
    }

    [Fact]
    public async Task InvalidNestedInputIsRejectedWithoutRoundingOrChangingSavedRecipe()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        var ingredient = await IngredientAsync(client);
        var original = await SaveAsync(client, product.Id, Input(ingredient.Id));
        RecipeRequest[] invalid = [
            Input(ingredient.Id) with { YieldQuantity = null }, Input(ingredient.Id) with { YieldQuantity = 0 }, Input(ingredient.Id) with { YieldQuantity = 10001 },
            Input(ingredient.Id) with { Instructions = new string('x', 2001) },
            new(1, null), new(1, []), new(1, [null]), new(1, [new(null, 1)]), new(1, [new(Guid.Empty, 1)]),
            new(1, [new(ingredient.Id, null)]), Input(ingredient.Id, 0), Input(ingredient.Id, -1), Input(ingredient.Id, 1000000), Input(ingredient.Id, 0.1501m),
            new(1, [new(ingredient.Id, 1), new(ingredient.Id, 2)]),
            new(1, Enumerable.Range(0, 101).Select(_ => (RecipeItemRequest?)new RecipeItemRequest(Guid.NewGuid(), 1)).ToArray())
        ];
        foreach (var input in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Path(product.Id), input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Path(product.Id), new
        {
            yieldQuantity = 1.5,
            items = new[] { new { ingredientId = ingredient.Id, quantity = 1 } }
        })).StatusCode);
        SameRecipe(original, (await client.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!);
    }

    [Fact]
    public async Task MaximumItemCountYieldAndQuantityAndMinimumQuantityRoundTrip()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        var ingredients = Enumerable.Range(0, 100).Select(index => new Ingredient
        {
            Name = "Item " + index,
            NormalizedName = "ITEM " + index,
            Unit = "kg",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }).ToArray();
        await factory.WithDatabaseAsync(async database => { database.Ingredients.AddRange(ingredients); await database.SaveChangesAsync(); });
        var saved = await SaveAsync(client, product.Id, new RecipeRequest(10000, ingredients.Select((ingredient, index) =>
            (RecipeItemRequest?)new RecipeItemRequest(ingredient.Id, index == 0 ? 0.001m : 999999.999m)).ToArray()));
        Assert.Equal(100, saved.Items.Length);
        Assert.Equal(10000, saved.YieldQuantity);
        SameRecipe(saved, (await client.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!);
    }

    [Fact]
    public async Task RepeatedPutKeepsOneRecipeAndOneIngredientLink()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        var ingredient = await IngredientAsync(client);
        var original = await SaveAsync(client, product.Id, Input(ingredient.Id));
        var repeated = await SaveAsync(client, product.Id, Input(ingredient.Id), false);
        Assert.Equal(original.Id, repeated.Id);
        Assert.Equal(original.CreatedAt, repeated.CreatedAt);
        Assert.Equal(original.Items, repeated.Items);
        await factory.WithDatabaseAsync(async database => { Assert.Equal(1, await database.Recipes.CountAsync()); Assert.Equal(1, await database.RecipeItems.CountAsync()); });
    }

    [Fact]
    public async Task ConcurrentFirstSavesSerializeAndNeverMixItems()
    {
        using var firstClient = await SignInAsync();
        using var secondClient = await SignInAsync();
        var product = await ProductAsync(firstClient);
        var first = await IngredientAsync(firstClient);
        var second = await IngredientAsync(firstClient);
        var responses = await Task.WhenAll(firstClient.PutAsJsonAsync(Path(product.Id), Input(first.Id, 1, 1)),
            secondClient.PutAsJsonAsync(Path(product.Id), Input(second.Id, 2, 2)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var final = (await firstClient.GetFromJsonAsync<RecipeResponse>(Path(product.Id)))!;
        var item = Assert.Single(final.Items);
        Assert.True(item.IngredientId == first.Id ? final.YieldQuantity == 1 && item.Quantity == 1 : item.IngredientId == second.Id && final.YieldQuantity == 2 && item.Quantity == 2);
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Recipes.CountAsync()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteRechecksSessionAndPermission(bool revokeSession)
    {
        var actor = await factory.CreateUserAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            var stored = await database.Users.SingleAsync(user => user.Id == actor.Id);
            if (revokeSession)
                stored.SecurityStamp = Guid.NewGuid();
            else
                stored.RoleId = 2;
            await database.SaveChangesAsync();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RecipeService>();
        var error = await Assert.ThrowsAsync<RecipeException>(() => service.SaveAsync(actor.Id, actor.SecurityStamp, Guid.NewGuid(), Input(Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(revokeSession ? RecipeError.InvalidSession : RecipeError.PermissionDenied, error.Error);
    }

    [Fact]
    public async Task DatabaseRestrictsReferencesAndInvalidAmountsAndCascadesOnlyRecipeItems()
    {
        using var client = await SignInAsync();
        var product = await ProductAsync(client);
        var ingredient = await IngredientAsync(client);
        var recipe = await SaveAsync(client, product.Id, Input(ingredient.Id));
        await factory.WithDatabaseAsync(async database =>
        {
            var ingredientError = await Assert.ThrowsAsync<PostgresException>(() => database.Ingredients.Where(item => item.Id == ingredient.Id).ExecuteDeleteAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ingredientError.SqlState);
            var productError = await Assert.ThrowsAsync<PostgresException>(() => database.Products.Where(item => item.Id == product.Id).ExecuteDeleteAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, productError.SqlState);
        });
        foreach (var sql in new[] { "UPDATE \"Recipes\" SET \"YieldQuantity\" = 0", "UPDATE \"RecipeItems\" SET \"Quantity\" = 0", "UPDATE \"RecipeItems\" SET \"Position\" = -1" })
            await factory.WithDatabaseAsync(async database =>
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(sql));
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            });
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Recipes.Where(item => item.Id == recipe.Id).ExecuteDeleteAsync();
            Assert.Empty(await database.RecipeItems.ToListAsync());
            Assert.Equal(1, await database.Products.CountAsync());
            Assert.Equal(1, await database.Ingredients.CountAsync());
        });
    }
}
