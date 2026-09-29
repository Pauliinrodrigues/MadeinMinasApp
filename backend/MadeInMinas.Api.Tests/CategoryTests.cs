using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Categories;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class CategoryTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
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
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        loginResponse.EnsureSuccessStatusCode();
        var login = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(roleId == 1, login.User.Permissions.Contains(AccessPolicies.ManageCatalog));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<CategoryResponse> CreateAsync(HttpClient client, string name = "Hambúrgueres", int order = 0, bool active = true)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, "Descrição", order, active));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorCanAccessCategories(int roleId)
    {
        using var client = roleId == 0 ? factory.CreateStaffClient() : await SignInAsync(roleId);
        var expected = roleId == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var id = Guid.NewGuid();
        Assert.Equal(expected, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(expected, (await client.GetAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Teste"))).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/categories/{id}", new UpdateCategoryRequest("Teste", null, 0, true))).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/categories/{id}/status", new CategoryStatusRequest(false))).StatusCode);
    }

    [Fact]
    public async Task CreateReadUpdateAndStatusPreserveDataAndIdentity()
    {
        using var client = await SignInAsync();
        var created = await CreateAsync(client, "  Hambúrgueres  ", 5);
        Assert.Equal("Hambúrgueres", created.Name);
        Assert.True(created.IsActive);
        Assert.NotEqual(default, created.CreatedAt);
        var get = await client.GetAsync($"/api/categories/{created.Id}");
        Assert.Equal(created, await get.Content.ReadFromJsonAsync<CategoryResponse>());
        Assert.DoesNotContain("normalizedName", await get.Content.ReadAsStringAsync());
        var update = await client.PutAsJsonAsync($"/api/categories/{created.Id}",
            new UpdateCategoryRequest("Lanches", "  Feitos na hora  ", 10, true));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var edited = (await update.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.Equal("Feitos na hora", edited.Description);
        Assert.Equal(10, edited.DisplayOrder);
        Assert.Equal(created.CreatedAt, edited.CreatedAt);
        Assert.True(edited.UpdatedAt >= edited.CreatedAt);
        foreach (var active in new[] { false, false, true })
        {
            var status = await client.PutAsJsonAsync($"/api/categories/{created.Id}/status", new CategoryStatusRequest(active));
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            var item = (await status.Content.ReadFromJsonAsync<CategoryResponse>())!;
            Assert.Equal(active, item.IsActive);
            Assert.Equal("Lanches", item.Name);
            Assert.Equal(10, item.DisplayOrder);
            Assert.Equal(created.CreatedAt, item.CreatedAt);
        }
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/categories/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DuplicateNamesIgnoreCaseOuterWhitespaceAndUnicodeCompositionEvenWhenInactive()
    {
        using var client = await SignInAsync();
        await CreateAsync(client, "Porções", active: false);
        foreach (var name in new[] { " PORÇÕES ", "Porc\u0327o\u0303es" })
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name)),
                HttpStatusCode.Conflict, "DuplicateCategoryName");
        var other = await CreateAsync(client, "Bebidas");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/categories/{other.Id}",
            new UpdateCategoryRequest("porções", null, 0, true)), HttpStatusCode.Conflict, "DuplicateCategoryName");
        Assert.Equal("Bebidas", (await client.GetFromJsonAsync<CategoryResponse>($"/api/categories/{other.Id}"))!.Name);
    }

    [Fact]
    public async Task ConcurrentCreatesPreserveUniqueName()
    {
        using var first = await SignInAsync();
        using var second = await SignInAsync();
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Bebidas")),
            second.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(" bebidas ")));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "DuplicateCategoryName");
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Categories.CountAsync()));
    }

    [Fact]
    public async Task PaginationFiltersAndDisplayOrderAreApplied()
    {
        using var client = await SignInAsync();
        var a = await CreateAsync(client, "Bebidas", 1);
        var b = await CreateAsync(client, "Batatas", 1, false);
        var c = await CreateAsync(client, "Lanches", 0);
        var first = (await client.GetFromJsonAsync<CategoryPageResponse>("/api/categories?pageSize=2"))!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new[] { c.Id, b.Id }, first.Items.Select(category => category.Id));
        var second = (await client.GetFromJsonAsync<CategoryPageResponse>("/api/categories?pageSize=2&page=2"))!;
        Assert.Equal(a.Id, Assert.Single(second.Items).Id);
        var inactive = (await client.GetFromJsonAsync<CategoryPageResponse>("/api/categories?isActive=false"))!;
        Assert.Equal(b.Id, Assert.Single(inactive.Items).Id);
        var search = (await client.GetFromJsonAsync<CategoryPageResponse>("/api/categories?search=BEB"))!;
        Assert.Equal(a.Id, Assert.Single(search.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<CategoryPageResponse>("/api/categories?search=inexistente"))!.Items);
    }

    public static TheoryData<string, string?, int> InvalidCategories => new()
    {
        { "", null, 0 }, { "   ", null, 0 }, { new string('x', 81), null, 0 },
        { "Categoria", new string('x', 501), 0 }, { "Categoria", null, -1 }, { "Categoria", null, 10000 }
    };

    [Theory]
    [MemberData(nameof(InvalidCategories))]
    public async Task InvalidCreateAndUpdateDoNotWrite(string name, string? description, int order)
    {
        using var client = await SignInAsync();
        var category = await CreateAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest(name, description, order))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/categories/{category.Id}",
            new UpdateCategoryRequest(name, description, order, true))).StatusCode);
        Assert.Equal(category, await client.GetFromJsonAsync<CategoryResponse>($"/api/categories/{category.Id}"));
    }

    [Fact]
    public async Task DefaultsAndRequiredUpdateFieldsAreExplicit()
    {
        using var client = await SignInAsync();
        var response = await client.PostAsJsonAsync("/api/categories", new { name = "Bebidas", description = "  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
        Assert.True(category.IsActive);
        Assert.Equal(0, category.DisplayOrder);
        Assert.Null(category.Description);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/categories/{category.Id}",
            new { name = "Bebidas", displayOrder = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/categories/{category.Id}",
            new { name = "Bebidas", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/categories/{category.Id}/status", new { })).StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?isActive=invalid")]
    [InlineData("?page=1000001")]
    public async Task InvalidListParametersReturnBadRequest(string query)
    {
        using var client = await SignInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/categories" + query)).StatusCode);
    }

    [Fact]
    public async Task MissingCategoryReturnsSafeNotFoundForEveryOperation()
    {
        using var client = await SignInAsync();
        var path = $"/api/categories/{Guid.NewGuid()}";
        await AssertProblemAsync(await client.GetAsync(path), HttpStatusCode.NotFound, "CategoryNotFound");
        await AssertProblemAsync(await client.PutAsJsonAsync(path, new UpdateCategoryRequest("Teste", null, 0, true)),
            HttpStatusCode.NotFound, "CategoryNotFound");
        await AssertProblemAsync(await client.PutAsJsonAsync(path + "/status", new CategoryStatusRequest(false)),
            HttpStatusCode.NotFound, "CategoryNotFound");
    }

    [Fact]
    public async Task WriteRevalidatesRevokedActorInsideTransaction()
    {
        var actor = await factory.CreateUserAsync();
        var oldStamp = actor.SecurityStamp;
        await factory.WithDatabaseAsync(async database =>
        {
            var user = await database.Users.SingleAsync(user => user.Id == actor.Id);
            user.SecurityStamp = Guid.NewGuid();
            await database.SaveChangesAsync();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<CategoryService>();
        var failure = await Assert.ThrowsAsync<CategoryException>(() =>
            service.CreateAsync(actor.Id, oldStamp, new CreateCategoryRequest("Não criar"), CancellationToken.None));
        Assert.Equal(CategoryError.InvalidSession, failure.Error);
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.Categories.ToListAsync()));
    }
}
