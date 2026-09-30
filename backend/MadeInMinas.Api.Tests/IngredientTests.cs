using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Ingredients;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class IngredientTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.Recipes.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });
    public Task DisposeAsync() => Task.CompletedTask;
    private static IngredientRequest Input(string name = "Carne", string unit = "kg", bool active = true) =>
        new(name, unit, 32.4567m, 1.125m, active, "  Fornecedor local  ");

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
    private static async Task<IngredientResponse> CreateAsync(HttpClient client, IngredientRequest? input = null)
    {
        var response = await client.PostAsJsonAsync("/api/ingredients", input ?? Input());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<IngredientResponse>())!;
    }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
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
    public async Task OnlyAdministratorCanAccessEveryOperation(int roleId)
    {
        using var client = roleId == 0 ? factory.CreateStaffClient() : await SignInAsync(roleId);
        var status = roleId == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var path = $"/api/ingredients/{Guid.NewGuid()}";
        Assert.Equal(status, (await client.GetAsync("/api/ingredients")).StatusCode);
        Assert.Equal(status, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync("/api/ingredients", Input())).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(path, Input())).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(path + "/status", new IngredientStatusRequest(false))).StatusCode);
    }

    [Theory]
    [InlineData("kg")]
    [InlineData("l")]
    [InlineData("un")]
    public async Task CreateEditReadAndStatusPreservePrecisionAndIdentity(string unit)
    {
        using var client = await SignInAsync();
        var item = await CreateAsync(client, Input("  Carne  ", unit));
        Assert.Equal("Carne", item.Name);
        Assert.Equal("Fornecedor local", item.Supplier);
        Assert.Equal(unit, item.Unit);
        Assert.Equal(32.4567m, item.UnitCost);
        Assert.Equal(1.125m, item.MinimumStock);
        var path = $"/api/ingredients/{item.Id}";
        Assert.Equal(item, await client.GetFromJsonAsync<IngredientResponse>(path));
        var response = await client.PutAsJsonAsync(path, Input("Carne fresca", unit) with
        {
            UnitCost = 0.0001m,
            MinimumStock = 0.001m,
            Supplier = " "
        });
        response.EnsureSuccessStatusCode();
        var edited = (await response.Content.ReadFromJsonAsync<IngredientResponse>())!;
        Assert.Null(edited.Supplier);
        Assert.Equal(item.CreatedAt, edited.CreatedAt);
        Assert.True(edited.UpdatedAt >= item.UpdatedAt);
        Assert.Equal(0.0001m, edited.UnitCost);
        Assert.Equal(0.001m, edited.MinimumStock);
        var inactiveResponse = await client.PutAsJsonAsync(path + "/status", new IngredientStatusRequest(false));
        var inactive = (await inactiveResponse.Content.ReadFromJsonAsync<IngredientResponse>())!;
        Assert.False(inactive.IsActive);
        var repeated = await client.PutAsJsonAsync(path + "/status", new IngredientStatusRequest(false));
        Assert.Equal(inactive, await repeated.Content.ReadFromJsonAsync<IngredientResponse>());
        var activeResponse = await client.PutAsJsonAsync(path + "/status", new IngredientStatusRequest(true));
        Assert.True((await activeResponse.Content.ReadFromJsonAsync<IngredientResponse>())!.IsActive);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(path)).StatusCode);
    }

    [Fact]
    public async Task CannotChangeUnitEvenWhenInactiveAndOtherEditsRollBack()
    {
        using var client = await SignInAsync();
        var item = await CreateAsync(client, Input(active: false));
        var path = $"/api/ingredients/{item.Id}";
        await ProblemAsync(await client.PutAsJsonAsync(path, Input("Outro nome", "un")), HttpStatusCode.Conflict, "IngredientUnitImmutable");
        Assert.Equal(item, await client.GetFromJsonAsync<IngredientResponse>(path));
    }

    [Fact]
    public async Task DuplicatesIncludingInactiveAndUnicodeAreRejectedOnCreateAndUpdate()
    {
        using var client = await SignInAsync();
        await CreateAsync(client, Input("Pão", "un", false));
        foreach (var name in new[] { " PÃO ", "Pa\u0303o" })
            await ProblemAsync(await client.PostAsJsonAsync("/api/ingredients", Input(name)), HttpStatusCode.Conflict, "DuplicateIngredientName");
        var other = await CreateAsync(client);
        await ProblemAsync(await client.PutAsJsonAsync($"/api/ingredients/{other.Id}", Input("pão")), HttpStatusCode.Conflict, "DuplicateIngredientName");
        Assert.Equal(other, await client.GetFromJsonAsync<IngredientResponse>($"/api/ingredients/{other.Id}"));
    }

    [Fact]
    public async Task ConcurrentDuplicateCreatesHaveOneWinner()
    {
        using var first = await SignInAsync();
        using var second = await SignInAsync();
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/ingredients", Input()), second.PostAsJsonAsync("/api/ingredients", Input(" CARNE ")));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        await ProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "DuplicateIngredientName");
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Ingredients.CountAsync()));
    }

    [Fact]
    public async Task AlphabeticPaginationSearchAndStatusFilter()
    {
        using var client = await SignInAsync();
        var c = await CreateAsync(client);
        var a = await CreateAsync(client, Input("Alface", active: false));
        var b = await CreateAsync(client, Input("Bacon"));
        var first = (await client.GetFromJsonAsync<IngredientPageResponse>("/api/ingredients?pageSize=2"))!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new[] { a.Id, b.Id }, first.Items.Select(item => item.Id));
        var second = (await client.GetFromJsonAsync<IngredientPageResponse>("/api/ingredients?pageSize=2&page=2"))!;
        Assert.Equal(c.Id, Assert.Single(second.Items).Id);
        Assert.Equal(a.Id, Assert.Single((await client.GetFromJsonAsync<IngredientPageResponse>("/api/ingredients?isActive=false&search=ALF"))!.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<IngredientPageResponse>("/api/ingredients?search=ausente"))!.Items);
    }

    [Fact]
    public async Task InvalidOrMissingFieldsDoNotWriteOrRound()
    {
        using var client = await SignInAsync();
        var item = await CreateAsync(client);
        IngredientRequest[] invalid = [
            Input() with { Name = " " }, Input() with { Name = new string('x', 121) },
            Input() with { Supplier = new string('x', 151) }, Input() with { Unit = "g" }, Input() with { Unit = "KG" },
            Input() with { Unit = "" }, Input() with { UnitCost = null }, Input() with { MinimumStock = null }, Input() with { IsActive = null },
            Input() with { UnitCost = -0.0001m }, Input() with { UnitCost = 1000000m }, Input() with { UnitCost = 1.00001m },
            Input() with { MinimumStock = -0.001m }, Input() with { MinimumStock = 1000000m }, Input() with { MinimumStock = 0.0001m }
        ];
        foreach (var request in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ingredients", request)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/ingredients/{item.Id}", request)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ingredients", new
        {
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/ingredients/{item.Id}/status", new
        {
        })).StatusCode);
        Assert.Equal(item, await client.GetFromJsonAsync<IngredientResponse>($"/api/ingredients/{item.Id}"));
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Ingredients.CountAsync()));
    }

    [Fact]
    public async Task ZeroAndMaximumCostsAndQuantitiesAreAccepted()
    {
        using var client = await SignInAsync();
        var zero = await CreateAsync(client, Input("Zero") with
        {
            UnitCost = 0,
            MinimumStock = 0
        });
        var maximum = await CreateAsync(client, Input("Máximo") with
        {
            UnitCost = 999999.9999m,
            MinimumStock = 999999.999m
        });
        Assert.Equal(zero, await client.GetFromJsonAsync<IngredientResponse>($"/api/ingredients/{zero.Id}"));
        Assert.Equal(maximum, await client.GetFromJsonAsync<IngredientResponse>($"/api/ingredients/{maximum.Id}"));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?isActive=invalid")]
    [InlineData("?page=1000001")]
    public async Task InvalidQueriesReturnBadRequest(string query)
    {
        using var client = await SignInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/ingredients" + query)).StatusCode);
    }

    [Fact]
    public async Task MissingIngredientReturnsNotFound()
    {
        using var client = await SignInAsync();
        var path = $"/api/ingredients/{Guid.NewGuid()}";
        await ProblemAsync(await client.GetAsync(path), HttpStatusCode.NotFound, "IngredientNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(path, Input()), HttpStatusCode.NotFound, "IngredientNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(path + "/status", new IngredientStatusRequest(false)), HttpStatusCode.NotFound, "IngredientNotFound");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteRechecksRevokedSessionOrChangedRole(bool revokeSession)
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
        var service = scope.ServiceProvider.GetRequiredService<IngredientService>();
        var failure = await Assert.ThrowsAsync<IngredientException>(() => service.CreateAsync(actor.Id, actor.SecurityStamp, Input(), CancellationToken.None));
        Assert.Equal(revokeSession ? IngredientError.InvalidSession : IngredientError.PermissionDenied, failure.Error);
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.Ingredients.ToListAsync()));
    }

    [Fact]
    public async Task DatabaseConstraintsRejectInvalidCostsQuantitiesAndUnits()
    {
        using var client = await SignInAsync();
        var item = await CreateAsync(client);
        foreach (var sql in new[] { "UPDATE \"Ingredients\" SET \"UnitCost\" = -1", "UPDATE \"Ingredients\" SET \"MinimumStock\" = -1", "UPDATE \"Ingredients\" SET \"Unit\" = 'g'" })
            await factory.WithDatabaseAsync(async database =>
            {
                var failure = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlRawAsync(sql));
                Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            });
        Assert.Equal(item, await client.GetFromJsonAsync<IngredientResponse>($"/api/ingredients/{item.Id}"));
    }
}
