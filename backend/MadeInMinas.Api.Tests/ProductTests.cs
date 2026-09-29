using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Categories;
using MadeInMinas.Api.DTOs.Products;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class ProductTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> SignInAsync(int roleId = 1)
    {
        var user = await factory.CreateUserAsync(roleId);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<CategoryResponse> CategoryAsync(HttpClient client, string name = "Lanches", bool active = true)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, IsActive: active));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private static ProductRequest Input(Guid categoryId, string name = "Uai Sô") => new(name, categoryId, 29.90m, true, true,
        "Pão, blend bovino, queijo e bacon.", "https://example.test/uai.png");

    private static async Task<ProductResponse> CreateAsync(HttpClient client, ProductRequest input)
    {
        var response = await client.PostAsJsonAsync("/api/products", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ProductOperationsRequireCatalogPermission(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var status = role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var path = $"/api/products/{Guid.NewGuid()}";
        Assert.Equal(status, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync("/api/products", Input(Guid.NewGuid()))).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(path, Input(Guid.NewGuid()))).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(path + "/status", new ProductStatusRequest(false))).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(path + "/availability", new ProductAvailabilityRequest(false))).StatusCode);
    }

    [Fact]
    public async Task CreateAndUpdatePreserveExactPriceAndReturnStoredData()
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        var input = Input(category.Id) with { Name = "  Uai Sô  " };
        var product = await CreateAsync(client, input);
        Assert.Equal("Uai Sô", product.Name);
        Assert.Equal(29.90m, product.Price);
        Assert.Equal(category.Name, product.CategoryName);
        Assert.True(product.IsAvailableForSale);
        var get = await client.GetAsync($"/api/products/{product.Id}");
        Assert.Equal(product, await get.Content.ReadFromJsonAsync<ProductResponse>());
        Assert.DoesNotContain("normalizedName", await get.Content.ReadAsStringAsync());
        var update = await client.PutAsJsonAsync($"/api/products/{product.Id}", input with
        { Name = "Uai Especial", Price = 0.01m, Description = "  ", ImageUrl = null });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var edited = (await update.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.Equal(0.01m, edited.Price);
        Assert.Equal(product.CreatedAt, edited.CreatedAt);
        Assert.Null(edited.Description);
        Assert.Null(edited.ImageUrl);
        Assert.Equal(edited, await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
    }

    public static TheoryData<decimal> InvalidPrices => new() { 0m, -1m, 1000000m, 1.001m, 29.999m };
    [Theory]
    [MemberData(nameof(InvalidPrices))]
    public async Task InvalidPricesAreRejectedWithoutRoundingOrWriting(decimal price)
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        var input = Input(category.Id);
        var product = await CreateAsync(client, input);
        var invalid = input with { Price = price };
        foreach (var response in new[] {
            await client.PostAsJsonAsync("/api/products", invalid),
            await client.PutAsJsonAsync($"/api/products/{product.Id}", invalid) })
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(product, await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"));
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Products.CountAsync()));
    }

    [Theory]
    [InlineData("http://example.test/photo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,abc")]
    [InlineData("https://user:password@example.test/image.png")]
    [InlineData("/image.png")]
    public async Task InvalidImageUrlsAreRejected(string imageUrl)
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", Input(category.Id) with { ImageUrl = imageUrl })).StatusCode);
    }

    [Fact]
    public async Task MissingFieldsAndInvalidLengthsReturnValidationErrors()
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        var valid = Input(category.Id);
        foreach (var invalid in new[] {
            valid with { Name = " " }, valid with { Name = new string('x', 121) },
            valid with { Description = new string('x', 1001) }, valid with { ImageUrl = new string('x', 2049) },
            valid with { CategoryId = null }, valid with { CategoryId = Guid.Empty }, valid with { Price = null },
            valid with { IsActive = null }, valid with { IsAvailable = null } })
        {
            var response = await client.PostAsJsonAsync("/api/products", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var product = await CreateAsync(client, valid);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/products/{product.Id}/status", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/products/{product.Id}/availability", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/products/{product.Id}",
            new { name = "Uai", categoryId = category.Id, price = 10 })).StatusCode);
    }

    [Fact]
    public async Task CategoryMustExistAndBeActiveForCreationOrMove()
    {
        using var client = await SignInAsync();
        var active = await CategoryAsync(client);
        var inactive = await CategoryAsync(client, "Inativos", false);
        await ProblemAsync(await client.PostAsJsonAsync("/api/products", Input(Guid.NewGuid())), HttpStatusCode.BadRequest, "InvalidProductCategory");
        await ProblemAsync(await client.PostAsJsonAsync("/api/products", Input(inactive.Id)), HttpStatusCode.BadRequest, "InactiveProductCategory");
        var product = await CreateAsync(client, Input(active.Id));
        await ProblemAsync(await client.PutAsJsonAsync($"/api/products/{product.Id}", Input(inactive.Id)), HttpStatusCode.BadRequest, "InactiveProductCategory");
        Assert.Equal(active.Id, (await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!.CategoryId);
    }

    [Fact]
    public async Task CategoryInactivationAffectsAvailabilityWithoutRewritingProducts()
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        var product = await CreateAsync(client, Input(category.Id));
        (await client.PutAsJsonAsync($"/api/categories/{category.Id}/status", new CategoryStatusRequest(false))).EnsureSuccessStatusCode();
        var unavailable = (await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!;
        Assert.True(unavailable.IsActive);
        Assert.True(unavailable.IsAvailable);
        Assert.False(unavailable.IsAvailableForSale);
        Assert.Equal(product.UpdatedAt, unavailable.UpdatedAt);
        Assert.Empty((await client.GetFromJsonAsync<ProductPageResponse>("/api/products?isAvailableForSale=true"))!.Items);
        Assert.Single((await client.GetFromJsonAsync<ProductPageResponse>("/api/products?isAvailableForSale=false"))!.Items);
        // É possível corrigir dados mantendo a categoria original, mesmo inativa.
        var updated = await client.PutAsJsonAsync($"/api/products/{product.Id}", Input(category.Id) with { Price = 30m });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        (await client.PutAsJsonAsync($"/api/categories/{category.Id}/status", new CategoryStatusRequest(true))).EnsureSuccessStatusCode();
        Assert.True((await client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!.IsAvailableForSale);
    }

    [Fact]
    public async Task ManualAvailabilityAndStatusAreIndependentAndIdempotent()
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        var product = await CreateAsync(client, Input(category.Id));
        var path = $"/api/products/{product.Id}";
        var paused = await client.PutAsJsonAsync(path + "/availability", new ProductAvailabilityRequest(false));
        var first = (await paused.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.True(first.IsActive);
        Assert.False(first.IsAvailableForSale);
        var repeated = await client.PutAsJsonAsync(path + "/availability", new ProductAvailabilityRequest(false));
        Assert.Equal(first, await repeated.Content.ReadFromJsonAsync<ProductResponse>());
        (await client.PutAsJsonAsync(path + "/status", new ProductStatusRequest(false))).EnsureSuccessStatusCode();
        var enabled = await client.PutAsJsonAsync(path + "/availability", new ProductAvailabilityRequest(true));
        Assert.False((await enabled.Content.ReadFromJsonAsync<ProductResponse>())!.IsAvailableForSale);
        var restored = await client.PutAsJsonAsync(path + "/status", new ProductStatusRequest(true));
        var restoredProduct = (await restored.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.True(restoredProduct.IsAvailableForSale);
        Assert.Equal(product.Price, restoredProduct.Price);
    }

    [Fact]
    public async Task NamesAreUniqueWithinCategoryIncludingInactiveAndMoves()
    {
        using var client = await SignInAsync();
        var first = await CategoryAsync(client);
        var second = await CategoryAsync(client, "Especiais");
        await CreateAsync(client, Input(first.Id) with { IsActive = false });
        await ProblemAsync(await client.PostAsJsonAsync("/api/products", Input(first.Id, " UAI SO\u0302 ")),
            HttpStatusCode.Conflict, "DuplicateProductName");
        var other = await CreateAsync(client, Input(second.Id));
        await ProblemAsync(await client.PutAsJsonAsync($"/api/products/{other.Id}", Input(first.Id)),
            HttpStatusCode.Conflict, "DuplicateProductName");
        Assert.Equal(second.Id, (await client.GetFromJsonAsync<ProductResponse>($"/api/products/{other.Id}"))!.CategoryId);
    }

    [Fact]
    public async Task ConcurrentDuplicatesReturnOneCreatedAndOneConflict()
    {
        using var first = await SignInAsync();
        using var second = await SignInAsync();
        var category = await CategoryAsync(first);
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/products", Input(category.Id)),
            second.PostAsJsonAsync("/api/products", Input(category.Id, " uai sô ")));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        await ProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "DuplicateProductName");
    }

    [Fact]
    public async Task FiltersPaginationAndStableSortingAreApplied()
    {
        using var client = await SignInAsync();
        var a = await CategoryAsync(client);
        var b = await CategoryAsync(client, "Bebidas");
        var first = await CreateAsync(client, Input(a.Id, "A lanche"));
        await CreateAsync(client, Input(a.Id, "B lanche") with { IsAvailable = false });
        await CreateAsync(client, Input(b.Id, "C bebida") with { IsActive = false });
        var page = (await client.GetFromJsonAsync<ProductPageResponse>("/api/products?pageSize=1&page=1"))!;
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal(2, (await client.GetFromJsonAsync<ProductPageResponse>($"/api/products?categoryId={a.Id}"))!.TotalCount);
        Assert.Equal(1, (await client.GetFromJsonAsync<ProductPageResponse>("/api/products?isActive=false"))!.TotalCount);
        Assert.Equal(2, (await client.GetFromJsonAsync<ProductPageResponse>("/api/products?isAvailableForSale=false"))!.TotalCount);
        Assert.Equal(2, (await client.GetFromJsonAsync<ProductPageResponse>("/api/products?search=LANCHE"))!.TotalCount);
        Assert.Empty((await client.GetFromJsonAsync<ProductPageResponse>("/api/products?page=4&pageSize=1"))!.Items);
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?categoryId=bad", "?isAvailableForSale=bad" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/products" + query)).StatusCode);
    }

    [Fact]
    public async Task MissingProductReturnsNotFoundForAllOperations()
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        var path = $"/api/products/{Guid.NewGuid()}";
        await ProblemAsync(await client.GetAsync(path), HttpStatusCode.NotFound, "ProductNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(path, Input(category.Id)), HttpStatusCode.NotFound, "ProductNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(path + "/status", new ProductStatusRequest(false)), HttpStatusCode.NotFound, "ProductNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(path + "/availability", new ProductAvailabilityRequest(false)), HttpStatusCode.NotFound, "ProductNotFound");
    }

    [Fact]
    public async Task ForeignKeyPreventsDeletingCategoryWithProducts()
    {
        using var client = await SignInAsync();
        var category = await CategoryAsync(client);
        await CreateAsync(client, Input(category.Id));
        await factory.WithDatabaseAsync(async database =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                database.Categories.Where(item => item.Id == category.Id).ExecuteDeleteAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        });
    }

    [Fact]
    public async Task RevokedActorCannotWriteAfterEarlierAuthorization()
    {
        var actor = await factory.CreateUserAsync();
        var stamp = actor.SecurityStamp;
        await factory.WithDatabaseAsync(async database =>
        {
            var user = await database.Users.SingleAsync(user => user.Id == actor.Id);
            user.SecurityStamp = Guid.NewGuid();
            await database.SaveChangesAsync();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<ProductException>(() =>
            scope.ServiceProvider.GetRequiredService<ProductService>().CreateAsync(actor.Id, stamp, Input(Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(ProductError.InvalidSession, error.Error);
    }
}
