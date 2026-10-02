using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Reports;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class SalesReportTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddDays(3);
    private readonly Customer customer = new() { Name = "Cliente privado", NormalizedName = "CLIENTE PRIVADO", Phone = "+5531999999999" };
    private readonly Product product = new() { Name = "Nome atual", NormalizedName = "NOME ATUAL", Price = 99m, Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" } };
    private User actor = null!;

    public async Task InitializeAsync()
    {
        await factory.WithDatabaseAsync(async database =>
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
            database.Customers.Add(customer);
            database.Products.Add(product);
            await database.SaveChangesAsync();
        });
        actor = await factory.CreateUserAsync();
    }

    public Task DisposeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });

    private Order AddOrder(AppDbContext database, DateTimeOffset createdAt, DateTimeOffset? confirmedAt = null, decimal price = 20m, int quantity = 1, bool cancelled = false)
    {
        var order = new Order
        {
            RequestId = Guid.NewGuid(),
            RequestHash = new string('a', 64),
            CreatedById = actor.Id,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerPhone = customer.Phone,
            Fulfillment = "Pickup",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Subtotal = price * quantity,
            Total = price * quantity,
            Items = [new OrderItem { ProductId = product.Id, ProductName = "Uai Sô", Position = 1, Quantity = quantity, UnitPrice = price, LineTotal = price * quantity }],
            History = [new OrderStatusHistory { Version = 1, ToStatus = "New", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = createdAt }]
        };
        if (confirmedAt is { } at)
        {
            order.Status = "Confirmed";
            order.Version = 2;
            order.UpdatedAt = at;
            order.History.Add(new OrderStatusHistory { Version = 2, FromStatus = "New", ToStatus = "Confirmed", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = at });
        }
        if (cancelled)
        {
            order.History.Add(new OrderStatusHistory { Version = ++order.Version, FromStatus = order.Status, ToStatus = "Cancelled", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = order.UpdatedAt.AddMinutes(1), Reason = "Cancelamento de teste" });
            order.Status = "Cancelled";
        }
        database.Orders.Add(order);
        return order;
    }

    private void AddPayment(AppDbContext database, Order order, string method, DateTimeOffset? receivedAt, DateTimeOffset? refundedAt = null)
    {
        var payment = new Payment
        {
            OrderId = order.Id,
            RequestId = Guid.NewGuid(),
            RequestHash = new string('b', 64),
            CreatedById = actor.Id,
            Method = method,
            Amount = order.Total,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.CreatedAt,
            History = [new PaymentStatusHistory { Version = 1, ToStatus = "Pending", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = order.CreatedAt }]
        };
        if (receivedAt is { } received)
        {
            payment.Status = "Received";
            payment.Version = 2;
            payment.UpdatedAt = received;
            payment.CashTendered = method == "Cash" ? order.Total + 50m : null;
            payment.History.Add(new PaymentStatusHistory { Version = 2, FromStatus = "Pending", ToStatus = "Received", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = received });
        }
        if (refundedAt is { } refunded)
        {
            payment.Status = "Refunded";
            payment.Version = 3;
            payment.UpdatedAt = refunded;
            payment.History.Add(new PaymentStatusHistory { Version = 3, FromStatus = "Received", ToStatus = "Refunded", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = refunded, Reason = "Devolução conferida" });
        }
        database.Payments.Add(payment);
    }

    private async Task<SalesReportResponse> ReadAsync(DateOnly? first = null, DateOnly? last = null)
    {
        SalesReportResponse result = null!;
        await factory.WithDatabaseAsync(async database =>
        {
            result = await new SalesReportService(database, new FixedClock(End.AddHours(-1))).GetAsync(
                new SalesReportQuery { StartDate = first ?? new DateOnly(2026, 9, 30), EndDate = last ?? new DateOnly(2026, 10, 2) }, CancellationToken.None);
            Assert.Empty(database.ChangeTracker.Entries());
        });
        return result;
    }

    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var user = role == 1 ? actor : await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        login.EnsureSuccessStatusCode();
        var result = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(role == 1, result.User.Permissions.Contains(AccessPolicies.ViewReports));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        return client;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ReportsRequireAdministrator(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var response = await client.GetAsync("/api/reports/sales");
        Assert.Equal(role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("confirmedValue", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("startDate=2026-10-01")]
    [InlineData("endDate=2026-10-01")]
    [InlineData("startDate=2026-10-02&endDate=2026-10-01")]
    [InlineData("startDate=2026-01-01&endDate=2026-04-01")]
    [InlineData("startDate=invalid&endDate=2026-10-01")]
    [InlineData("startDate=2026-02-30&endDate=2026-03-01")]
    [InlineData("startDate=9999-12-31&endDate=9999-12-31")]
    public async Task InvalidOrExcessivePeriodsReturnValidationProblem(string query)
    {
        using var client = await SignInAsync();
        var response = await client.GetAsync("/api/reports/sales?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task DefaultPeriodHasSevenBrasiliaDaysAndEmptyMetricsDoNotInventAverages()
    {
        using var client = await SignInAsync();
        var response = await client.GetAsync("/api/reports/sales");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var report = (await response.Content.ReadFromJsonAsync<SalesReportResponse>())!;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(report.CalculatedAt, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).DateTime);
        Assert.Equal(today, report.EndDate);
        Assert.Equal(today.AddDays(-6), report.StartDate);
        Assert.Equal(7, report.Days.Length);
        Assert.Equal(new SalesMetricsResponse(0, 0, 0, 0, null, 0, 0, 0), report.Summary);
        Assert.All(report.Days, day => Assert.Equal(report.Summary, day.Metrics));
        Assert.Equal(4, report.PaymentMethods.Length);
        Assert.Empty(report.TopProducts);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/reports/sales", new { })).StatusCode);
        await factory.WithDatabaseAsync(async database => await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.IsActive, false)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reports/sales")).StatusCode);
    }

    [Fact]
    public async Task NinetyDaysAreAcceptedWithoutDroppingEmptyDates()
    {
        using var client = await SignInAsync();
        var response = await client.GetAsync("/api/reports/sales?startDate=2026-01-01&endDate=2026-03-31");
        response.EnsureSuccessStatusCode();
        var report = (await response.Content.ReadFromJsonAsync<SalesReportResponse>())!;
        Assert.Equal(90, report.Days.Length);
        Assert.Equal(new DateOnly(2026, 1, 1), report.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 3, 31), report.Days[^1].Date);
    }

    [Fact]
    public async Task InclusiveCivilDatesUseExclusiveUtcEndAndGroupAfterMidnightUtcCorrectly()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            foreach (var at in new[] { Start.AddMilliseconds(-1), Start, Start.AddDays(1).AddMilliseconds(-1), Start.AddDays(1), End.AddMilliseconds(-1), End })
                AddOrder(database, at, at);
            await database.SaveChangesAsync();
        });
        var report = await ReadAsync();
        Assert.Equal(Start, report.StartsAt);
        Assert.Equal(End, report.EndsAt);
        Assert.Equal(new[] { 2, 1, 1 }, report.Days.Select(day => day.Metrics.Created));
        Assert.Equal(new[] { 2, 1, 1 }, report.Days.Select(day => day.Metrics.Confirmed));
        Assert.Equal(4, report.Summary.Confirmed);
        Assert.Equal(80m, report.Summary.ConfirmedValue);
    }

    [Fact]
    public async Task SummaryUsesWeightedTicketHistoryDatesAndDeliveryFeeWithoutCancelledOrders()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var delivered = AddOrder(database, Start.AddDays(-2), Start, price: 10m);
            var address = new Address { CustomerId = customer.Id, Street = "Rua privada", Number = "1", Neighborhood = "Centro", City = "BH", State = "MG" };
            database.Addresses.Add(address);
            delivered.Fulfillment = "Delivery";
            delivered.AddressId = address.Id;
            delivered.AddressStreet = address.Street;
            delivered.AddressNumber = address.Number;
            delivered.AddressNeighborhood = address.Neighborhood;
            delivered.AddressCity = address.City;
            delivered.AddressState = address.State;
            delivered.DeliveryFee = 4.5m;
            delivered.Total += delivered.DeliveryFee;
            AddOrder(database, Start.AddDays(1), Start.AddDays(1), price: 20m);
            AddOrder(database, Start.AddDays(1), Start.AddDays(1), price: 40m);
            AddOrder(database, Start.AddDays(1), Start.AddDays(1), price: 99m, cancelled: true);
            AddOrder(database, Start.AddDays(1));
            await database.SaveChangesAsync();
        });
        var report = await ReadAsync();
        Assert.Equal(new SalesMetricsResponse(4, 1, 3, 74.5m, 24.83m, 0, 0, 0), report.Summary);
        Assert.Equal(14.5m, report.Days[0].Metrics.AverageTicket);
        Assert.Equal(30m, report.Days[1].Metrics.AverageTicket);
        Assert.Null(report.Days[2].Metrics.AverageTicket);
        Assert.Equal(70m, Assert.Single(report.TopProducts).ItemValue);
    }

    [Fact]
    public async Task ReceiptsFollowEventDatesAndMethodsPreservingRefundsAndIgnoringCashTendered()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            AddPayment(database, AddOrder(database, Start.AddDays(-2)), "Cash", Start, Start.AddDays(1));
            AddPayment(database, AddOrder(database, Start.AddDays(-2), price: 40m), "Pix", Start.AddDays(-1), Start.AddDays(1));
            AddPayment(database, AddOrder(database, Start.AddDays(-2), price: 60m), "CreditCard", Start.AddDays(1), End);
            AddPayment(database, AddOrder(database, Start.AddDays(-2), price: 80m), "DebitCard", End.AddMilliseconds(-1));
            AddPayment(database, AddOrder(database, Start, price: 500m), "Pix", End);
            AddPayment(database, AddOrder(database, Start, price: 500m), "Cash", null);
            await database.SaveChangesAsync();
        });
        var report = await ReadAsync();
        Assert.Equal(160m, report.Summary.Received);
        Assert.Equal(60m, report.Summary.Refunded);
        Assert.Equal(100m, report.Summary.NetReceived);
        Assert.Equal(new[] { 20m, 60m, 80m }, report.Days.Select(day => day.Metrics.Received));
        Assert.Equal(new[] { 0m, 60m, 0m }, report.Days.Select(day => day.Metrics.Refunded));
        Assert.Equal(new PaymentMethodReceiptsResponse("Cash", 20, 20, 0), report.PaymentMethods[0]);
        Assert.Equal(new PaymentMethodReceiptsResponse("Pix", 0, 40, -40), report.PaymentMethods[1]);
        Assert.Equal(new PaymentMethodReceiptsResponse("CreditCard", 60, 0, 60), report.PaymentMethods[2]);
        Assert.Equal(new PaymentMethodReceiptsResponse("DebitCard", 80, 0, 80), report.PaymentMethods[3]);
        Assert.Equal(report.Summary.NetReceived, report.PaymentMethods.Sum(item => item.NetReceived));
    }

    [Fact]
    public async Task RankingUsesSnapshotsGroupsRepeatedLinesAndLimitsToTenWithStableTies()
    {
        var ids = Enumerable.Range(1, 12).Select(index => Guid.Parse($"00000000-0000-0000-0000-{index:D12}")).ToArray();
        await factory.WithDatabaseAsync(async database =>
        {
            for (var index = 0; index < ids.Length; index++)
            {
                database.Products.Add(new Product { Id = ids[index], Name = "Atual " + index, NormalizedName = "ATUAL " + index, Price = 99m, IsActive = false, CategoryId = product.CategoryId });
                var order = AddOrder(database, Start, Start);
                order.Items[0].ProductId = ids[index];
                order.Items[0].ProductName = "Histórico " + index;
            }
            var repeated = AddOrder(database, Start.AddDays(1), Start.AddDays(1));
            repeated.Items[0].ProductId = ids[11];
            repeated.Items[0].ProductName = "Nome posterior";
            repeated.Items.Add(new OrderItem { ProductId = ids[11], ProductName = "Nome posterior", Position = 2, Quantity = 2, UnitPrice = 15, LineTotal = 30 });
            repeated.Subtotal = repeated.Total = 50;
            AddOrder(database, Start, Start, quantity: 99, cancelled: true);
            AddOrder(database, Start, quantity: 99);
            await database.SaveChangesAsync();
        });
        var report = await ReadAsync();
        Assert.Equal(10, report.TopProducts.Length);
        Assert.Equal(new SalesReportProductResponse(ids[11], "Nome posterior", 4, 70), report.TopProducts[0]);
        Assert.Equal(ids.Take(9), report.TopProducts.Skip(1).Select(item => item.ProductId));
    }

    [Fact]
    public async Task OneDayMatchesDashboardAndQueriesDoNotChangeCommercialDataOrLeakCustomerData()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var order = AddOrder(database, Start.AddDays(2), Start.AddDays(2));
            AddPayment(database, order, "Pix", Start.AddDays(2));
            AddOrder(database, Start.AddDays(2), Start.AddDays(2), cancelled: true);
            await database.SaveChangesAsync();
        });
        var report = await ReadAsync(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2));
        await factory.WithDatabaseAsync(async database =>
        {
            var before = JsonSerializer.Serialize(await database.Orders.AsNoTracking().Include(order => order.Items).Include(order => order.History).OrderBy(order => order.Number).ToArrayAsync());
            var dashboard = await new DashboardService(database, new FixedClock(End.AddHours(-1))).TodayAsync(CancellationToken.None);
            Assert.Equal(dashboard.Orders.Created, report.Summary.Created);
            Assert.Equal(dashboard.Orders.CreatedAndCancelled, report.Summary.CreatedAndCancelled);
            Assert.Equal(dashboard.Orders.Confirmed, report.Summary.Confirmed);
            Assert.Equal(dashboard.Orders.ConfirmedValue, report.Summary.ConfirmedValue);
            Assert.Equal(dashboard.Orders.AverageTicket, report.Summary.AverageTicket);
            Assert.Equal(dashboard.Receipts.Received, report.Summary.Received);
            Assert.Equal(dashboard.Receipts.Refunded, report.Summary.Refunded);
            Assert.Equal(dashboard.Receipts.NetReceived, report.Summary.NetReceived);
            Assert.Equal(dashboard.TopProducts.Select(item => item.ProductId), report.TopProducts.Select(item => item.ProductId));
            await new SalesReportService(database, new FixedClock(End.AddHours(-1))).GetAsync(new SalesReportQuery(), CancellationToken.None);
            Assert.Equal(before, JsonSerializer.Serialize(await database.Orders.AsNoTracking().Include(order => order.Items).Include(order => order.History).OrderBy(order => order.Number).ToArrayAsync()));
            Assert.Single(await database.Payments.ToListAsync());
            Assert.Equal(2, await database.PaymentStatusHistory.CountAsync());
            Assert.Empty(await database.StockMovements.ToListAsync());
        });
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain(customer.Phone, json);
        Assert.DoesNotContain(customer.Name, json);
        Assert.DoesNotContain("Customer", json);
    }

    [Fact]
    public async Task HistoricalDaylightSavingGapUsesFirstExistingTimeAndCorrectLocalGrouping()
    {
        var firstInstant = new DateTimeOffset(2018, 11, 4, 3, 0, 0, TimeSpan.Zero);
        await factory.WithDatabaseAsync(async database =>
        {
            foreach (var at in new[] { firstInstant.AddMilliseconds(-1), firstInstant, firstInstant.AddHours(23).AddMilliseconds(-1), firstInstant.AddHours(23) })
                AddOrder(database, at, at);
            await database.SaveChangesAsync();
        });
        var report = await ReadAsync(new DateOnly(2018, 11, 4), new DateOnly(2018, 11, 4));
        Assert.Equal(firstInstant, report.StartsAt);
        Assert.Equal(firstInstant.AddHours(23), report.EndsAt);
        Assert.Equal(2, report.Summary.Confirmed);
        Assert.Equal(2, Assert.Single(report.Days).Metrics.Created);
    }

    [Fact]
    public async Task GroupingDoesNotDependOnPostgreSqlSessionTimeZone()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var order = AddOrder(database, Start.AddDays(1).AddMinutes(-1), Start.AddDays(1).AddMinutes(-1));
            AddPayment(database, order, "Pix", Start.AddDays(1).AddMinutes(-1));
            await database.SaveChangesAsync();
        });
        await factory.WithDatabaseAsync(async database =>
        {
            await database.Database.OpenConnectionAsync();
            try
            {
                await database.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Pacific/Auckland'");
                var report = await new SalesReportService(database, new FixedClock(End)).GetAsync(
                    new SalesReportQuery { StartDate = new DateOnly(2026, 9, 30), EndDate = new DateOnly(2026, 10, 2) }, CancellationToken.None);
                Assert.Equal(1, report.Days[0].Metrics.Created);
                Assert.Equal(1, report.Days[0].Metrics.Confirmed);
                Assert.Equal(20m, report.Days[0].Metrics.Received);
                Assert.Equal(0, report.Days[1].Metrics.Created);
                Assert.Equal(0m, report.Days[1].Metrics.Received);
            }
            finally { await database.Database.CloseConnectionAsync(); }
        });
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
