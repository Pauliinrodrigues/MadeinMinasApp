using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.PublicCart;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.DTOs.StoreAttendance;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class StoreAttendanceTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database => await database.StoreAttendanceSettings.ExecuteDeleteAsync());
    public Task DisposeAsync() => factory.WithDatabaseAsync(async database => await database.StoreAttendanceSettings.ExecuteDeleteAsync());
    private async Task<HttpClient> LoginAsync(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }
    private static StoreAttendanceData Defaults() => new(
        Enumerable.Range(1, 7).Select(day => new StoreOpeningHours(day, "18:30", day <= 4 ? "23:30" : "00:00")).ToArray(),
        ["Card", "Cash", "Pix"], "O prazo de entrega pode variar de acordo com a demanda.", false, false);

    [Theory]
    [InlineData("2026-10-12T21:29:59Z", false, "2026-10-12T21:30:00Z")]
    [InlineData("2026-10-12T21:30:00Z", true, "2026-10-13T02:30:00Z")]
    [InlineData("2026-10-13T02:29:59Z", true, "2026-10-13T02:30:00Z")]
    [InlineData("2026-10-13T02:30:00Z", false, "2026-10-13T21:30:00Z")]
    [InlineData("2026-10-15T21:30:00Z", true, "2026-10-16T02:30:00Z")]
    [InlineData("2026-10-16T21:30:00Z", true, "2026-10-17T03:00:00Z")]
    [InlineData("2026-10-17T02:59:59Z", true, "2026-10-17T03:00:00Z")]
    [InlineData("2026-10-17T03:00:00Z", false, "2026-10-17T21:30:00Z")]
    [InlineData("2026-10-18T21:30:00Z", true, "2026-10-19T03:00:00Z")]
    [InlineData("2026-10-19T03:00:00Z", false, "2026-10-19T21:30:00Z")]
    public void ScheduleUsesBrasiliaAndClosesExactlyAtTheEnd(string timestamp, bool open, string nextTimestamp)
    {
        var result = StoreAttendanceService.Evaluate(Defaults(), DateTimeOffset.Parse(timestamp));
        Assert.Equal(open, result.IsOpen);
        Assert.Equal(open, result.AcceptingOrders);
        Assert.Equal(DateTimeOffset.Parse(nextTimestamp), result.NextChangeAt);
        Assert.Equal("America/Sao_Paulo", result.TimeZone);
    }

    [Fact]
    public void PauseWinsOverOutsideHoursAndClosedDaysCanHaveNoNextOpening()
    {
        var allClosed = Defaults() with { Hours = Defaults().Hours.Select(hour => hour with { IsClosed = true }).ToArray(), ReceiveOrdersOutsideHours = true };
        var result = StoreAttendanceService.Evaluate(allClosed, DateTimeOffset.Parse("2026-10-10T22:00:00Z"));
        Assert.False(result.IsOpen);
        Assert.True(result.AcceptingOrders);
        Assert.Null(result.NextChangeAt);
        Assert.False(StoreAttendanceService.Evaluate(allClosed with { IsPaused = true }, result.CheckedAt).AcceptingOrders);
    }

    [Fact]
    public async Task PublicSettingsHaveConfirmedCommercialDataWithoutInternalAuditOrWrites()
    {
        using var guest = factory.CreateStaffClient();
        var response = await guest.GetAsync("/api/store-attendance/public");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var data = (await response.Content.ReadFromJsonAsync<PublicStoreAttendanceResponse>())!;
        Assert.Equal(7, data.Hours.Length);
        Assert.Equal("23:30", data.Hours[0].ClosesAt);
        Assert.Equal("00:00", data.Hours[4].ClosesAt);
        Assert.Equal(new[] { "Card", "Cash", "Pix" }, data.PaymentMethods);
        Assert.Equal(Defaults().DeliveryEstimateMessage, data.DeliveryEstimateMessage);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Null(json["updatedBy"]);
        Assert.Null(json["revision"]);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.StoreAttendanceSettings.AnyAsync()));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorsCanReadOrSaveInternalSettings(int role)
    {
        using var staff = await LoginAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/store-attendance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/store-attendance", new SaveStoreAttendanceRequest(new string('A', 64), Defaults()))).StatusCode);
    }

    [Fact]
    public async Task SavedSettingsPersistAndRetryIsIdempotentButConcurrentChangesRequireReload()
    {
        using var admin = await LoginAsync();
        var current = (await admin.GetFromJsonAsync<StoreAttendanceResponse>("/api/store-attendance"))!;
        var input = new SaveStoreAttendanceRequest(current.Revision, current.Settings with { IsPaused = true });
        var first = await admin.PutAsJsonAsync("/api/store-attendance", input);
        first.EnsureSuccessStatusCode();
        var saved = (await first.Content.ReadFromJsonAsync<StoreAttendanceResponse>())!;
        Assert.True(saved.Settings.IsPaused);
        Assert.NotEqual(current.Revision, saved.Revision);
        var repeated = await admin.PutAsJsonAsync("/api/store-attendance", input);
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(saved.Revision, (await repeated.Content.ReadFromJsonAsync<StoreAttendanceResponse>())!.Revision);
        var conflict = await admin.PutAsJsonAsync("/api/store-attendance", input with { Settings = current.Settings });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("StoreSettingsChanged", JsonNode.Parse(await conflict.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, (await database.StoreAttendanceSettings.SingleAsync()).Version));
    }

    [Fact]
    public async Task RejectsInvalidSchedulesPaymentsAndUnknownFields()
    {
        using var admin = await LoginAsync();
        var current = (await admin.GetFromJsonAsync<StoreAttendanceResponse>("/api/store-attendance"))!;
        var invalid = new[] {
            Defaults() with { Hours = Defaults().Hours.Select(hour => hour with { Day = 1 }).ToArray() },
            Defaults() with { Hours = Defaults().Hours.Select(hour => hour with { OpensAt = "25:00" }).ToArray() },
            Defaults() with { Hours = Defaults().Hours.Select(hour => hour with { ClosesAt = "17:00" }).ToArray() },
            Defaults() with { PaymentMethods = ["Cash", "Cash"] },
            Defaults() with { PaymentMethods = ["BankTransfer"] },
            Defaults() with { IsPaused = null }
        };
        foreach (var settings in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/store-attendance", new SaveStoreAttendanceRequest(current.Revision, settings))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/store-attendance", new { current.Revision, settings = Defaults(), invented = true })).StatusCode);
        await factory.WithDatabaseAsync(async database => Assert.False(await database.StoreAttendanceSettings.AnyAsync()));
    }

    [Fact]
    public async Task PausedStoreRejectsReviewAndNewSubmissionWithoutCreatingCustomersOrOrders()
    {
        using var admin = await LoginAsync();
        var current = (await admin.GetFromJsonAsync<StoreAttendanceResponse>("/api/store-attendance"))!;
        (await admin.PutAsJsonAsync("/api/store-attendance", new SaveStoreAttendanceRequest(current.Revision, current.Settings with { IsPaused = true }))).EnsureSuccessStatusCode();
        using var guest = factory.CreateStaffClient();
        Assert.False((await guest.GetFromJsonAsync<PublicStoreAttendanceResponse>("/api/store-attendance/public"))!.AcceptingOrders);
        var checkout = new PublicCheckoutRequest("Pessoa de teste", "31999998888", new PublicCartQuoteRequest([new(Guid.NewGuid(), 1)]));
        foreach (var response in new[] {
            await guest.PostAsJsonAsync("/api/public-checkout/review", checkout),
            await guest.PostAsJsonAsync("/api/public-checkout/orders", new PublicOrderRequest(Guid.NewGuid(), new string('A', 64), checkout)) })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("StoreNotAcceptingOrders", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        }
        await factory.WithDatabaseAsync(async database => { Assert.False(await database.Orders.AnyAsync()); Assert.False(await database.Customers.AnyAsync()); });
    }
}
