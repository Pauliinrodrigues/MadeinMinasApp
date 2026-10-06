using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Ingredients;
using MadeInMinas.Api.DTOs.Stock;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class StockReplenishmentTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private const string Path = "/api/stock/replenishment";
    private readonly Ingredient[] ingredients =
    [
        Item("Bacon", "kg", 0, 2, 0, supplier: "Fazenda Central"),
        Item("Carne", "kg", 1.125m, 2.5m, 1, supplier: "Fazenda Central"),
        Item("Pão", "un", 10, 10, 1),
        Item("Molho", "l", 3, 1, 1, supplier: "Molhos da Serra"),
        Item("Copo", "un", 0, 0, 1, supplier: "Embalagens"),
        Item("Queijo", "kg", 0, 3, 0, false, "Fazenda Central"),
        Item("Etiqueta", "un", 4, 0, 0),
        Item("Bebida", "l", 0.001m, 0.002m, 1, supplier: "Distribuidor")
    ];

    private static Ingredient Item(string name, string unit, decimal balance, decimal minimum, long version, bool active = true, string? supplier = null) =>
        new()
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Unit = unit,
            CurrentStock = balance,
            MinimumStock = minimum,
            StockVersion = version,
            IsActive = active,
            Supplier = supplier,
            UnitCost = 9.9999m
        };

    private static async Task ClearAsync(AppDbContext database)
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Ingredients.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    }

    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await ClearAsync(database);
        database.Ingredients.AddRange(ingredients);
        await database.SaveChangesAsync();
    });

    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    private static async Task<StockReplenishmentResponse> ReadAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(Path + query);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<StockReplenishmentResponse>())!;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorCanRead(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        Assert.Equal(role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, (await client.GetAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task SummaryPrioritizesZeroAndKeepsMinimumBoundaryWithoutWriting()
    {
        using var client = await SignInAsync();
        var result = await ReadAsync(client);
        Assert.Equal(new StockReplenishmentSummary(7, 6, 2, 5, 2), result.Summary);
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(new[] { "Bacon", "Copo", "Bebida", "Carne", "Pão", "Etiqueta" }, result.Items.Select(item => item.Name));
        Assert.All(result.Items, item => Assert.True(item.IsActive));
        var meat = Assert.Single(result.Items, item => item.Name == "Carne");
        Assert.Equal(1.375m, meat.QuantityToMinimum);
        Assert.Equal("kg", meat.Unit);
        Assert.Equal("Fazenda Central", meat.Supplier);
        Assert.True(meat.HasMovements);
        var bread = Assert.Single(result.Items, item => item.Name == "Pão");
        Assert.True(bread.IsLowStock);
        Assert.Equal(0m, bread.QuantityToMinimum);
        var cup = Assert.Single(result.Items, item => item.Name == "Copo");
        Assert.True(cup.IsLowStock);
        Assert.Equal(0m, cup.QuantityToMinimum);
        Assert.Equal(0.001m, Assert.Single(result.Items, item => item.Name == "Bebida").QuantityToMinimum);
        Assert.False(Assert.Single(result.Items, item => item.Name == "Etiqueta").HasMovements);
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(ingredients.Length, await database.Ingredients.CountAsync());
            Assert.Empty(await database.StockMovements.ToArrayAsync());
            foreach (var original in ingredients)
            {
                var current = await database.Ingredients.SingleAsync(item => item.Id == original.Id);
                Assert.Equal(original.CurrentStock, current.CurrentStock);
                Assert.Equal(original.StockVersion, current.StockVersion);
                Assert.Equal(original.UnitCost, current.UnitCost);
                Assert.Equal(original.UpdatedAt, current.UpdatedAt);
            }
        });
    }

    [Fact]
    public async Task EveryQueueRetainsSummaryAndCanIncludeInactiveIngredients()
    {
        using var client = await SignInAsync();
        foreach (var (status, count) in new[] { ("Attention", 6), ("OutOfStock", 2), ("LowStock", 5), ("Unrecorded", 2), ("All", 7) })
        {
            var result = await ReadAsync(client, "?status=" + status);
            Assert.Equal(count, result.TotalCount);
            Assert.Equal(count, result.Items.Length);
            Assert.Equal(new StockReplenishmentSummary(7, 6, 2, 5, 2), result.Summary);
            var including = await ReadAsync(client, "?status=" + status + "&includeInactive=true");
            Assert.Equal(count + 1, including.TotalCount);
            Assert.False(Assert.Single(including.Items, item => item.Name == "Queijo").IsActive);
            Assert.Equal(new StockReplenishmentSummary(8, 7, 3, 6, 3), including.Summary);
        }
        Assert.Equal(0m, Assert.Single((await ReadAsync(client, "?status=All")).Items, item => item.Name == "Molho").QuantityToMinimum);
    }

    [Fact]
    public async Task SearchByNameOrSupplierCombinesWithQueuesAndPagination()
    {
        using var client = await SignInAsync();
        var first = await ReadAsync(client, "?pageSize=2");
        var second = await ReadAsync(client, "?pageSize=2&page=2");
        Assert.Equal(6, first.TotalCount);
        Assert.Equal(6, second.TotalCount);
        Assert.Equal(new[] { "Bacon", "Copo" }, first.Items.Select(item => item.Name));
        Assert.Equal(new[] { "Bebida", "Carne" }, second.Items.Select(item => item.Name));
        Assert.Empty((await ReadAsync(client, "?pageSize=2&page=4")).Items);
        var supplier = await ReadAsync(client, "?search=%20fazenda%20&status=Unrecorded&includeInactive=true&pageSize=1");
        Assert.Equal(2, supplier.TotalCount);
        Assert.Equal(new StockReplenishmentSummary(3, 3, 2, 3, 2), supplier.Summary);
        Assert.Equal("Bacon", Assert.Single(supplier.Items).Name);
        Assert.Equal("Queijo", Assert.Single((await ReadAsync(client, "?search=fazenda&status=Unrecorded&includeInactive=true&pageSize=1&page=2")).Items).Name);
        Assert.Equal("Pão", Assert.Single((await ReadAsync(client, "?search=" + Uri.EscapeDataString("pa\u0303o"))).Items).Name);
        var noAttention = await ReadAsync(client, "?search=serra");
        Assert.Empty(noAttention.Items);
        Assert.Equal(new StockReplenishmentSummary(1, 0, 0, 0, 0), noAttention.Summary);
        Assert.Equal("Molho", Assert.Single((await ReadAsync(client, "?search=SERRA&status=All")).Items).Name);
        var absent = await ReadAsync(client, "?search=nonexistent");
        Assert.Equal(0, absent.TotalCount);
        Assert.Equal(new StockReplenishmentSummary(0, 0, 0, 0, 0), absent.Summary);
    }

    [Fact]
    public async Task InvalidQueriesAreRejectedAndEmptyInventoryHasZeroCounters()
    {
        using var client = await SignInAsync();
        foreach (var query in new[] { "page=0", "page=1000001", "pageSize=0", "pageSize=101", "status=lowstock", "status=", "includeInactive=yes", "search=" + new string('x', 151) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Path + "?" + query)).StatusCode);
        await factory.WithDatabaseAsync(async database => await database.Ingredients.ExecuteDeleteAsync());
        var empty = await ReadAsync(client);
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        Assert.Equal(new StockReplenishmentSummary(0, 0, 0, 0, 0), empty.Summary);
    }

    [Fact]
    public async Task EntryMinimumChangeAndInactivationAreReflectedByNextRead()
    {
        using var client = await SignInAsync();
        var ingredient = ingredients[0];
        var entry = new StockMovementRequest(Guid.NewGuid(), 0, "Entry", 3, "Reposição em teste");
        (await client.PostAsJsonAsync($"/api/ingredients/{ingredient.Id}/stock/movements", entry)).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await ReadAsync(client)).Items, item => item.IngredientId == ingredient.Id);
        var item = Assert.Single((await ReadAsync(client, "?search=bacon&status=All")).Items);
        Assert.True(item.HasMovements);
        Assert.Equal(3, item.CurrentStock);
        var edit = new IngredientRequest(ingredient.Name, ingredient.Unit, ingredient.UnitCost, 4, true, ingredient.Supplier);
        (await client.PutAsJsonAsync($"/api/ingredients/{ingredient.Id}", edit)).EnsureSuccessStatusCode();
        Assert.Equal(1m, Assert.Single((await ReadAsync(client, "?search=bacon")).Items).QuantityToMinimum);
        (await client.PutAsJsonAsync($"/api/ingredients/{ingredient.Id}/status", new IngredientStatusRequest(false))).EnsureSuccessStatusCode();
        Assert.Empty((await ReadAsync(client, "?search=bacon")).Items);
        Assert.False(Assert.Single((await ReadAsync(client, "?search=bacon&includeInactive=true")).Items).IsActive);
        await factory.WithDatabaseAsync(async database => Assert.Single(await database.StockMovements.ToArrayAsync()));
    }
}
