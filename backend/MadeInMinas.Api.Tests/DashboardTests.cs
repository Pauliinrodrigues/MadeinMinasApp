using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Dashboard;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class DashboardTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddDays(1);
    private readonly Customer customer = new() { Name = "Cliente privado", NormalizedName = "CLIENTE PRIVADO", Phone = "+5531999999999" };
    private readonly Product product = new()
    {
        Name = "Nome atual",
        NormalizedName = "NOME ATUAL",
        Price = 80m,
        Category = new Category { Name = "Lanches", NormalizedName = "LANCHES" }
    };
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
        // Não deixar vínculos comerciais para as outras suítes que compartilham o banco isolado.
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });

    private Order AddOrder(AppDbContext database, DateTimeOffset createdAt, int quantity = 1)
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
            Subtotal = 20m * quantity,
            Total = 20m * quantity,
            Items = [new OrderItem { ProductId = product.Id, ProductName = "Uai Sô", Position = 1, Quantity = quantity, UnitPrice = 20m, LineTotal = 20m * quantity }],
            History = [new OrderStatusHistory { Version = 1, ToStatus = "New", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = createdAt }]
        };
        database.Orders.Add(order);
        return order;
    }

    private void Move(Order order, string status, DateTimeOffset at)
    {
        order.History.Add(new OrderStatusHistory
        {
            Version = ++order.Version,
            FromStatus = order.Status,
            ToStatus = status,
            ActorId = actor.Id,
            ActorName = actor.Name,
            OccurredAt = at,
            Reason = status == "Cancelled" ? "Cancelamento de teste" : null
        });
        order.Status = status;
        order.UpdatedAt = at;
    }

    private Payment AddPayment(AppDbContext database, Order order, DateTimeOffset? receivedAt, DateTimeOffset? refundedAt = null, bool cash = false)
    {
        var payment = new Payment
        {
            OrderId = order.Id,
            RequestId = Guid.NewGuid(),
            RequestHash = new string('b', 64),
            CreatedById = actor.Id,
            Method = cash ? "Cash" : "Pix",
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
            payment.CashTendered = cash ? order.Total + 50m : null;
            payment.History.Add(new PaymentStatusHistory { Version = 2, FromStatus = "Pending", ToStatus = "Received", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = received });
        }
        if (refundedAt is { } refunded)
        {
            payment.Status = "Refunded";
            payment.Version = 3;
            payment.UpdatedAt = refunded;
            payment.History.Add(new PaymentStatusHistory { Version = 3, FromStatus = "Received", ToStatus = "Refunded", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = refunded, Reason = "Estorno de teste" });
        }
        database.Payments.Add(payment);
        return payment;
    }

    private async Task<DailyDashboardResponse> ReadAsync(DateTimeOffset? now = null)
    {
        DailyDashboardResponse result = null!;
        await factory.WithDatabaseAsync(async database =>
        {
            result = await new DashboardService(database, new FixedClock(now ?? Start.AddHours(12))).TodayAsync(CancellationToken.None);
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
        var response = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(role == 1, response.User.Permissions.Contains(AccessPolicies.ViewDashboard));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        return client;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorCanReadDashboard(int role)
    {
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        var response = await client.GetAsync("/api/dashboard/today");
        Assert.Equal(role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("confirmedValue", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EndpointIsReadOnlyUncachedAndEmptyAveragesAreUnknown()
    {
        using var client = await SignInAsync();
        var response = await client.GetAsync("/api/dashboard/today");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var data = (await response.Content.ReadFromJsonAsync<DailyDashboardResponse>())!;
        Assert.Equal(new DailyOrdersResponse(0, 0, 0, 0m, null), data.Orders);
        Assert.Equal(new DailyReceiptsResponse(0, 0, 0), data.Receipts);
        Assert.Equal(new DailyProductionResponse(0, null), data.Production);
        Assert.Empty(data.TopProducts);
        Assert.Equal(7, data.Queues.Length);
        Assert.All(data.Queues, queue => Assert.Equal(0, queue.Count));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/dashboard/today", new { total = 1 })).StatusCode);
        await factory.WithDatabaseAsync(async database => await database.Users.Where(user => user.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(user => user.IsActive, false)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/today")).StatusCode);
    }

    [Fact]
    public async Task CivilDayUsesBrasiliaAndExclusiveEndEvenWhenUtcDateHasChanged()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            foreach (var at in new[] { Start.AddMilliseconds(-1), Start, End.AddMilliseconds(-1), End })
                AddOrder(database, at);
            await database.SaveChangesAsync();
        });
        var data = await ReadAsync(End.AddHours(-1));
        Assert.Equal(new DateOnly(2026, 10, 2), data.Date);
        Assert.Equal("America/Sao_Paulo", data.TimeZone);
        Assert.Equal(Start, data.StartsAt);
        Assert.Equal(End, data.EndsAt);
        Assert.Equal(End.AddHours(-1), data.CalculatedAt);
        Assert.Equal(2, data.Orders.Created);
        Assert.Equal(4, data.Queues.Single(queue => queue.Status == "New").Count);
        Assert.Equal(new DateOnly(2026, 10, 3), (await ReadAsync(End)).Date);
    }

    [Fact]
    public async Task ConfirmationsUseHistoryExcludeCancellationsAndIncludeDeliveryFee()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var old = AddOrder(database, Start.AddDays(-1));
            Move(old, "Confirmed", Start);
            var address = new Address { CustomerId = customer.Id, Street = "Rua privada", Number = "1", Neighborhood = "Centro", City = "BH", State = "MG" };
            database.Addresses.Add(address);
            old.Fulfillment = "Delivery";
            old.AddressId = address.Id;
            old.AddressStreet = address.Street;
            old.AddressNumber = address.Number;
            old.AddressNeighborhood = address.Neighborhood;
            old.AddressCity = address.City;
            old.AddressState = address.State;
            old.DeliveryFee = 5.01m;
            old.Total += old.DeliveryFee;
            var today = AddOrder(database, Start.AddHours(1), 2);
            Move(today, "Confirmed", End.AddMilliseconds(-1));
            var cancelled = AddOrder(database, Start);
            Move(cancelled, "Confirmed", Start.AddMinutes(1));
            Move(cancelled, "Cancelled", Start.AddMinutes(2));
            AddOrder(database, Start);
            var tomorrow = AddOrder(database, Start);
            Move(tomorrow, "Confirmed", End);
            var yesterday = AddOrder(database, Start.AddDays(-1));
            Move(yesterday, "Confirmed", Start.AddMilliseconds(-1));
            await database.SaveChangesAsync();
        });
        var data = await ReadAsync();
        Assert.Equal(new DailyOrdersResponse(4, 1, 2, 65.01m, 32.51m), data.Orders);
        Assert.Equal(3, Assert.Single(data.TopProducts).Quantity);
        Assert.Equal(60m, data.TopProducts[0].ItemValue);
        Assert.Equal(0, data.Receipts.Received);
    }

    [Fact]
    public async Task ReceiptAndRefundDatesAreIndependentOfOrderAndCurrentPaymentStatus()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            AddPayment(database, AddOrder(database, Start.AddDays(-1)), Start, cash: true);
            AddPayment(database, AddOrder(database, Start.AddDays(-1), 2), Start.AddHours(1), Start.AddHours(2));
            AddPayment(database, AddOrder(database, Start.AddDays(-2), 3), Start.AddMilliseconds(-1), End.AddMilliseconds(-1));
            AddPayment(database, AddOrder(database, Start.AddDays(-2), 4), Start.AddHours(1), End);
            AddPayment(database, AddOrder(database, Start, 5), End);
            AddPayment(database, AddOrder(database, Start, 6), null);
            var cancelled = AddPayment(database, AddOrder(database, Start, 7), null);
            cancelled.Status = "Cancelled";
            cancelled.Version = 2;
            cancelled.History.Add(new PaymentStatusHistory { Version = 2, FromStatus = "Pending", ToStatus = "Cancelled", ActorId = actor.Id, ActorName = actor.Name, OccurredAt = Start.AddHours(1), Reason = "Desistência" });
            await database.SaveChangesAsync();
        });
        var data = await ReadAsync();
        Assert.Equal(new DailyReceiptsResponse(140m, 100m, 40m), data.Receipts);
        Assert.Equal(0m, data.Orders.ConfirmedValue);
    }

    [Fact]
    public async Task RefundFromAnEarlierDayCanProduceNegativeNetReceipts()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            AddPayment(database, AddOrder(database, Start.AddDays(-2)), Start.AddDays(-1), Start);
            await database.SaveChangesAsync();
        });
        Assert.Equal(new DailyReceiptsResponse(0, 20, -20), (await ReadAsync()).Receipts);
    }

    [Fact]
    public async Task QueuesIncludePreviousDaysAndExcludeFinalizedAndCancelled()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            string[] stages = ["New", "Confirmed", "InPreparation", "Ready", "AwaitingDelivery", "OutForDelivery", "Delivered", "Finalized"];
            for (var last = 0; last < stages.Length; last++)
            {
                var order = AddOrder(database, Start.AddDays(-2));
                for (var step = 1; step <= last; step++)
                    Move(order, stages[step], Start.AddDays(-1).AddMinutes(step));
            }
            Move(AddOrder(database, Start.AddDays(-2)), "Cancelled", Start);
            await database.SaveChangesAsync();
        });
        var data = await ReadAsync();
        Assert.Equal(0, data.Orders.Created);
        Assert.Equal(0, data.Orders.Confirmed);
        Assert.All(data.Queues, queue => Assert.Equal(1, queue.Count));
        Assert.DoesNotContain(data.Queues, queue => queue.Status is "Finalized" or "Cancelled");
    }

    [Fact]
    public async Task ProductionMeasuresCompletedWorkAcrossMidnightIncludingLaterCancellation()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            foreach (var (started, ready, cancel) in new[]
            {
                (Start.AddMinutes(-15), Start.AddMinutes(15), false),
                (Start.AddHours(1), Start.AddHours(1).AddMinutes(15), true),
                (Start.AddHours(-2), Start.AddMilliseconds(-1), false),
                (End.AddMinutes(-10), End, false)
            })
            {
                var order = AddOrder(database, Start.AddDays(-1));
                Move(order, "Confirmed", Start.AddHours(-3));
                Move(order, "InPreparation", started);
                Move(order, "Ready", ready);
                if (cancel)
                    Move(order, "Cancelled", ready.AddMinutes(1));
            }
            var ongoing = AddOrder(database, Start);
            Move(ongoing, "Confirmed", Start);
            Move(ongoing, "InPreparation", Start);
            await database.SaveChangesAsync();
        });
        Assert.Equal(new DailyProductionResponse(2, 22.5m), (await ReadAsync()).Production);
    }

    [Fact]
    public async Task ProductionWithoutValidStartDoesNotInventDuration()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            foreach (var missing in new[] { true, false })
            {
                var order = AddOrder(database, Start);
                Move(order, "Confirmed", Start);
                Move(order, "InPreparation", Start.AddHours(2));
                Move(order, "Ready", Start.AddHours(1));
                if (missing)
                    order.History.RemoveAll(history => history.ToStatus == "InPreparation");
            }
            await database.SaveChangesAsync();
        });
        Assert.Equal(new DailyProductionResponse(0, null), (await ReadAsync()).Production);
    }

    [Fact]
    public async Task RankingCombinesSameProductLinesAndHistoricalNamesWithoutUsingCurrentCatalog()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var first = AddOrder(database, Start, 2);
            first.Items[0].ProductName = "Nome antigo";
            first.Items.Add(new OrderItem { ProductId = product.Id, ProductName = "Nome antigo", Position = 2, Quantity = 1, UnitPrice = 15m, LineTotal = 15m });
            first.Subtotal += 15m;
            first.Total = first.Subtotal;
            Move(first, "Confirmed", Start.AddMinutes(1));
            var later = AddOrder(database, Start.AddHours(1));
            Move(later, "Confirmed", Start.AddHours(1));
            var cancelled = AddOrder(database, Start, 99);
            Move(cancelled, "Confirmed", Start);
            Move(cancelled, "Cancelled", Start);
            AddOrder(database, Start, 99);
            await database.Products.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false).SetProperty(item => item.Price, 999m));
            await database.SaveChangesAsync();
        });
        var top = Assert.Single((await ReadAsync()).TopProducts);
        Assert.Equal(new DashboardProductResponse(product.Id, "Uai Sô", 4, 75m), top);
    }

    [Fact]
    public async Task RankingHasFiveProductsOrderedByQuantityThenItemValueThenId()
    {
        var ids = Enumerable.Range(1, 7).Select(index => Guid.Parse($"00000000-0000-0000-0000-{index:D12}")).ToArray();
        await factory.WithDatabaseAsync(async database =>
        {
            for (var index = 0; index < ids.Length; index++)
            {
                database.Products.Add(new Product { Id = ids[index], Name = "Produto " + index, NormalizedName = "PRODUTO " + index, Price = 20m, CategoryId = product.CategoryId });
                var order = AddOrder(database, Start, index == 6 ? 3 : 2);
                order.Items[0].ProductId = ids[index];
                order.Items[0].ProductName = "Produto " + index;
                if (index == 5)
                {
                    order.Items[0].UnitPrice = 30m;
                    order.Items[0].LineTotal = 60m;
                    order.Subtotal = order.Total = 60m;
                }
                Move(order, "Confirmed", Start);
            }
            await database.SaveChangesAsync();
        });
        Assert.Equal(new[] { ids[6], ids[5], ids[0], ids[1], ids[2] }, (await ReadAsync()).TopProducts.Select(item => item.ProductId));
    }

    [Fact]
    public async Task RepeatedReadsPreserveOrdersPaymentsHistoryAndStockAndExposeNoCustomerData()
    {
        await factory.WithDatabaseAsync(async database =>
        {
            var order = AddOrder(database, Start);
            Move(order, "Confirmed", Start);
            AddPayment(database, order, Start);
            await database.SaveChangesAsync();
        });
        async Task<string> SnapshotAsync()
        {
            var snapshot = "";
            await factory.WithDatabaseAsync(async database => snapshot = JsonSerializer.Serialize(new
            {
                Orders = await database.Orders.AsNoTracking().Include(order => order.Items).Include(order => order.History).ToArrayAsync(),
                Payments = await database.Payments.AsNoTracking().Include(payment => payment.History).ToArrayAsync(),
                Stock = await database.StockMovements.AsNoTracking().ToArrayAsync()
            }));
            return snapshot;
        }
        var before = await SnapshotAsync();
        var data = await ReadAsync();
        await ReadAsync();
        Assert.Equal(before, await SnapshotAsync());
        var json = JsonSerializer.Serialize(data);
        Assert.DoesNotContain(customer.Name, json);
        Assert.DoesNotContain(customer.Phone, json);
        Assert.DoesNotContain("Customer", json);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
