using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Ingredients;
using MadeInMinas.Api.DTOs.Stock;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class StockTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Ingredient ingredient = new()
    {
        Name = "Carne",
        NormalizedName = "CARNE",
        Unit = "kg",
        UnitCost = 35.1234m,
        MinimumStock = 2.5m
    };
    private string Path => $"/api/ingredients/{ingredient.Id}/stock";
    private static StockMovementRequest Input(long version = 0, string type = "Entry", decimal quantity = 10m) =>
        new(Guid.NewGuid(), version, type, quantity, "  Conferência de estoque  ");

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
        database.Ingredients.Add(ingredient);
        await database.SaveChangesAsync();
    });
    public Task DisposeAsync() => factory.WithDatabaseAsync(async database => await database.StockMovements.ExecuteDeleteAsync());

    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }
    private async Task<StockMovementResponse> WriteAsync(HttpClient client, StockMovementRequest input)
    {
        var response = await client.PostAsJsonAsync(Path + "/movements", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<StockMovementResponse>())!;
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
    public async Task OnlyAdministratorCanReadOrWrite(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var status = role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(status, (await client.GetAsync(Path)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync(Path + "/movements", Input())).StatusCode);
    }

    [Fact]
    public async Task EntryExitAndPhysicalCountPreserveAuditAndDoNotChangeCostOrCatalogTimestamp()
    {
        using var client = await SignInAsync();
        var initial = (await client.GetFromJsonAsync<StockResponse>(Path))!;
        Assert.Equal(0m, initial.CurrentStock);
        Assert.True(initial.IsLowStock);
        Assert.Empty(initial.Movements);
        var entry = await WriteAsync(client, Input(quantity: 10.125m));
        var exit = await WriteAsync(client, Input(1, "Exit", 1.001m));
        var count = await WriteAsync(client, Input(2, "Count", 2.5m));
        Assert.Equal(10.125m, entry.Delta);
        Assert.Equal(-1.001m, exit.Delta);
        Assert.Equal(9.124m, count.PreviousBalance);
        Assert.Equal(-6.624m, count.Delta);
        Assert.Equal(2.5m, count.Balance);
        Assert.Equal("Conferência de estoque", count.Reason);
        Assert.Equal("Test staff", count.ActorName);
        Assert.Equal("Carne", count.IngredientName);
        Assert.Equal("kg", count.Unit);
        Assert.NotEqual(Guid.Empty, count.ActorId);
        var page = (await client.GetFromJsonAsync<StockResponse>(Path + "?pageSize=2"))!;
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new long[] { 3, 2 }, page.Movements.Select(item => item.Version));
        Assert.Equal(2.5m, page.CurrentStock);
        Assert.True(page.IsLowStock);
        Assert.Equal(entry, Assert.Single((await client.GetFromJsonAsync<StockResponse>(Path + "?pageSize=2&page=2"))!.Movements));
        var zero = await WriteAsync(client, Input(3, "Count", 0));
        Assert.Equal(-2.5m, zero.Delta);
        await factory.WithDatabaseAsync(async database =>
        {
            var current = await database.Ingredients.SingleAsync();
            Assert.Equal(ingredient.UnitCost, current.UnitCost);
            Assert.Equal(ingredient.UpdatedAt, current.UpdatedAt);
            Assert.Equal(4, current.StockVersion);
            Assert.Equal(current.CurrentStock, await database.StockMovements.SumAsync(item => item.Delta));
            Assert.Empty(await database.Orders.ToListAsync());
        });
    }

    [Fact]
    public async Task DuplicateConcurrentSubmissionAndLaterRetryWriteExactlyOnce()
    {
        using var client = await SignInAsync();
        var input = Input();
        var results = await Task.WhenAll(WriteAsync(client, input), WriteAsync(client, input));
        Assert.Equal(results[0], results[1]);
        await WriteAsync(client, Input(1, "Exit", 2));
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Users.ExecuteUpdateAsync(setter => setter.SetProperty(user => user.Name, "Nome novo"));
            await database.Ingredients.ExecuteUpdateAsync(setter => setter.SetProperty(item => item.Name, "Carne nova").SetProperty(item => item.IsActive, false));
        });
        Assert.Equal(results[0], await WriteAsync(client, input));
        var stock = (await client.GetFromJsonAsync<StockResponse>(Path))!;
        Assert.Equal(8m, stock.CurrentStock);
        Assert.Equal(2, stock.TotalCount);
        Assert.Equal("Carne", stock.Movements[1].IngredientName);
        Assert.Equal("Test staff", stock.Movements[1].ActorName);
    }

    [Fact]
    public async Task ConcurrentDifferentCommandsRequireReviewAndCannotOverdraw()
    {
        using var client = await SignInAsync();
        await WriteAsync(client, Input(quantity: 5));
        var responses = await Task.WhenAll(client.PostAsJsonAsync(Path + "/movements", Input(1, "Exit", 4)),
            client.PostAsJsonAsync(Path + "/movements", Input(1, "Exit", 4)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        await ProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "StockVersionConflict");
        await ProblemAsync(await client.PostAsJsonAsync(Path + "/movements", Input(2, "Exit", 4)), HttpStatusCode.Conflict, "InsufficientStock");
        var stock = (await client.GetFromJsonAsync<StockResponse>(Path))!;
        Assert.Equal(1m, stock.CurrentStock);
        Assert.Equal(2, stock.TotalCount);
    }

    [Fact]
    public async Task RequestIdCannotBeReusedWithOtherContentOrByAnotherActor()
    {
        using var client = await SignInAsync();
        using var other = await SignInAsync();
        var input = Input();
        await WriteAsync(client, input);
        foreach (var changed in new[] { input with { Quantity = 9 }, input with { Reason = "Outro" }, input with { Type = "Count" }, input with { ExpectedVersion = 1 } })
            await ProblemAsync(await client.PostAsJsonAsync(Path + "/movements", changed), HttpStatusCode.Conflict, "StockRequestConflict");
        await ProblemAsync(await other.PostAsJsonAsync(Path + "/movements", input), HttpStatusCode.Conflict, "StockRequestConflict");
        Assert.Equal(1, (await client.GetFromJsonAsync<StockResponse>(Path))!.TotalCount);
    }

    [Fact]
    public async Task InvalidPrecisionMissingAndUnknownFieldsNeverWrite()
    {
        using var client = await SignInAsync();
        var input = Input();
        StockMovementRequest[] invalid = [input with { RequestId = Guid.Empty }, input with { ExpectedVersion = null }, input with { ExpectedVersion = -1 },
            input with { Type = "Adjustment" }, input with { Type = "" }, input with { Quantity = null }, input with { Quantity = 0 },
            input with { Quantity = -1 }, input with { Quantity = 0.0001m }, input with { Quantity = 1000000 },
            input with { Reason = " " }, input with { Reason = new string('x', 501) }];
        foreach (var request in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path + "/movements", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path + "/movements", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path + "/movements", new { input.RequestId, input.ExpectedVersion, input.Type, input.Quantity, input.Reason, balance = 100 })).StatusCode);
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=1000001" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Path + query)).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<StockResponse>(Path))!.TotalCount);
    }

    [Fact]
    public async Task InsufficientOverflowUnchangedAndInactiveWritesRollBack()
    {
        using var client = await SignInAsync();
        await ProblemAsync(await client.PostAsJsonAsync(Path + "/movements", Input(type: "Exit", quantity: 0.001m)), HttpStatusCode.Conflict, "InsufficientStock");
        await ProblemAsync(await client.PostAsJsonAsync(Path + "/movements", Input(type: "Count", quantity: 0)), HttpStatusCode.Conflict, "StockUnchanged");
        await WriteAsync(client, Input(quantity: 999999.999m));
        await ProblemAsync(await client.PostAsJsonAsync(Path + "/movements", Input(1, quantity: 0.001m)), HttpStatusCode.Conflict, "StockLimitExceeded");
        await factory.WithDatabaseAsync(async database => await database.Ingredients.ExecuteUpdateAsync(setter => setter.SetProperty(item => item.IsActive, false)));
        await ProblemAsync(await client.PostAsJsonAsync(Path + "/movements", Input(1, "Exit", 1)), HttpStatusCode.Conflict, "InactiveIngredient");
        var stock = (await client.GetFromJsonAsync<StockResponse>(Path))!;
        Assert.Equal(999999.999m, stock.CurrentStock);
        Assert.Equal(1, stock.TotalCount);
        Assert.False(stock.IsLowStock);
    }

    [Fact]
    public async Task CatalogEditingPreservesStockAndMovementsCannotBeEditedOrDeleted()
    {
        using var client = await SignInAsync();
        var movement = await WriteAsync(client, Input());
        var response = await client.PutAsJsonAsync($"/api/ingredients/{ingredient.Id}", new IngredientRequest("Outra carne", "kg", 40, 3, true));
        response.EnsureSuccessStatusCode();
        var stock = (await client.GetFromJsonAsync<StockResponse>(Path))!;
        Assert.Equal(10m, stock.CurrentStock);
        Assert.Equal(1, stock.Version);
        Assert.Equal("Carne", Assert.Single(stock.Movements).IngredientName);
        Assert.Equal("Outra carne", stock.Name);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Path + "/movements")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync(Path + "/movements", Input())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Path + "/movements/" + movement.Id)).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransactionRevalidatesSessionAndAdministratorRole(bool revokeSession)
    {
        var actor = await factory.CreateUserAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            var stored = await database.Users.SingleAsync();
            if (revokeSession)
                stored.SecurityStamp = Guid.NewGuid();
            else
                stored.RoleId = 2;
            await database.SaveChangesAsync();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<StockService>();
        var failure = await Assert.ThrowsAsync<StockException>(() => service.CreateAsync(actor.Id, actor.SecurityStamp, ingredient.Id, Input(), CancellationToken.None));
        Assert.Equal(revokeSession ? StockError.InvalidSession : StockError.PermissionDenied, failure.Error);
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task UnknownIngredientReturnsNotFound()
    {
        using var client = await SignInAsync();
        var missing = $"/api/ingredients/{Guid.NewGuid()}/stock";
        await ProblemAsync(await client.GetAsync(missing), HttpStatusCode.NotFound, "IngredientNotFound");
        await ProblemAsync(await client.PostAsJsonAsync(missing + "/movements", Input()), HttpStatusCode.NotFound, "IngredientNotFound");
    }
}
