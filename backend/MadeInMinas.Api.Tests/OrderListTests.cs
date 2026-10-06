using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Orders;
using MadeInMinas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class OrderListTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    private readonly Customer customer = new() { Name = "Maria", NormalizedName = "MARIA", Phone = "+5531999991234" };
    private readonly List<Order> orders = [];
    private static readonly (string Origin, string Status, string[] Payments, string Summary, bool Unpaid)[] Cases =
    [
        ("Manual", "New", [], "NotRegistered", true),
        ("DirectLink", "Confirmed", ["Pending"], "Pending", true),
        ("Manual", "Ready", ["Received"], "Received", false),
        ("DirectLink", "Delivered", ["Refunded"], "Refunded", true),
        ("Manual", "New", ["Cancelled"], "NotRegistered", true),
        ("DirectLink", "Cancelled", [], "NotDue", false),
        ("Manual", "Cancelled", ["Refunded"], "Refunded", false),
        ("DirectLink", "Finalized", ["Refunded"], "Refunded", false),
        ("Manual", "Confirmed", ["Refunded", "Cancelled", "Pending"], "Pending", true),
        ("DirectLink", "Ready", ["Refunded", "Cancelled", "Received"], "Received", false),
        ("Manual", "Finalized", ["Received"], "Received", false),
        ("DirectLink", "Cancelled", ["Cancelled"], "NotDue", false)
    ];

    private static async Task ClearAsync(AppDbContext database)
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    }

    public async Task InitializeAsync()
    {
        await factory.WithDatabaseAsync(ClearAsync);
        var actor = await factory.CreateUserAsync();
        await factory.WithDatabaseAsync(async database =>
        {
            database.Customers.Add(customer);
            for (var index = 0; index < Cases.Length; index++)
            {
                var scenario = Cases[index];
                var createdAt = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero).AddMinutes(index);
                var order = new Order
                {
                    RequestId = Guid.NewGuid(),
                    RequestHash = new string('A', 64),
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    CustomerPhone = customer.Phone,
                    Origin = scenario.Origin,
                    CreatedById = scenario.Origin == "Manual" ? actor.Id : null,
                    Fulfillment = "Pickup",
                    Status = scenario.Status,
                    Subtotal = 20,
                    Total = 20,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                };
                database.Orders.Add(order);
                orders.Add(order);
                for (var attempt = 0; attempt < scenario.Payments.Length; attempt++)
                    database.Payments.Add(new Payment
                    {
                        OrderId = order.Id,
                        CreatedById = actor.Id,
                        RequestId = Guid.NewGuid(),
                        RequestHash = new string('B', 64),
                        Method = "Pix",
                        Status = scenario.Payments[attempt],
                        Amount = 20,
                        CreatedAt = createdAt.AddSeconds(attempt),
                        UpdatedAt = createdAt.AddSeconds(attempt)
                    });
            }
            await database.SaveChangesAsync();
        });
    }

    public Task DisposeAsync() => factory.WithDatabaseAsync(ClearAsync);

    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var actor = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(actor.Username, factory.Password));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    private static async Task<OrderPageResponse> ReadAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/orders" + query);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<OrderPageResponse>())!;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SummaryUsesCurrentPaymentAndDoesNotDuplicateOrdersOrWrite(int role)
    {
        using var client = await SignInAsync(role);
        var page = await ReadAsync(client);
        Assert.Equal(Cases.Length, page.TotalCount);
        Assert.Equal(orders.AsEnumerable().Reverse().Select(order => order.Id), page.Items.Select(item => item.Id));
        for (var index = 0; index < Cases.Length; index++)
        {
            var item = Assert.Single(page.Items, item => item.Id == orders[index].Id);
            Assert.Equal(Cases[index].Origin, item.Origin);
            Assert.Equal(Cases[index].Summary, item.PaymentStatus);
        }
        await factory.WithDatabaseAsync(async database =>
        {
            Assert.Equal(Cases.Length, await database.Orders.CountAsync());
            Assert.Equal(Cases.Sum(scenario => scenario.Payments.Length), await database.Payments.CountAsync());
            Assert.Equal(Cases.Length * 20m, await database.Orders.SumAsync(order => order.Total));
            Assert.All(await database.Orders.ToArrayAsync(), order => Assert.Equal(1, order.Version));
        });
    }

    [Fact]
    public async Task PaymentFiltersIncludeMissingAndRefundedPaymentsButExcludeTerminalOrdersFromUnpaid()
    {
        using var client = await SignInAsync();
        foreach (var status in new[] { "Unpaid", "NotRegistered", "Pending", "Received", "Refunded", "NotDue" })
        {
            var expected = orders.Where((_, index) => status == "Unpaid" ? Cases[index].Unpaid : Cases[index].Summary == status).Reverse().ToArray();
            var response = await ReadAsync(client, "?paymentStatus=" + status);
            Assert.Equal(expected.Length, response.TotalCount);
            Assert.Equal(expected.Select(order => order.Id), response.Items.Select(item => item.Id));
        }
    }

    [Fact]
    public async Task OriginCombinesWithSearchStatusCustomerAndPaginationBeforeCounting()
    {
        using var client = await SignInAsync();
        var first = await ReadAsync(client, "?origin=DirectLink&paymentStatus=Unpaid&pageSize=1");
        var second = await ReadAsync(client, "?origin=DirectLink&paymentStatus=Unpaid&pageSize=1&page=2");
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(orders[3].Id, Assert.Single(first.Items).Id);
        Assert.Equal(orders[1].Id, Assert.Single(second.Items).Id);
        var combined = await ReadAsync(client, $"?origin=DirectLink&paymentStatus=Pending&status=Confirmed&customerId={customer.Id}&search=maria");
        Assert.Equal(orders[1].Id, Assert.Single(combined.Items).Id);
        var byNumber = await ReadAsync(client, $"?origin=Manual&paymentStatus=Received&search=%23{orders[2].Number}");
        Assert.Equal(orders[2].Id, Assert.Single(byNumber.Items).Id);
        Assert.Empty((await ReadAsync(client, "?origin=Manual&paymentStatus=NotDue&status=New")).Items);
        Assert.Empty((await ReadAsync(client, $"?origin=Manual&customerId={Guid.NewGuid()}")).Items);
        Assert.Empty((await ReadAsync(client, "?origin=DirectLink&paymentStatus=Unpaid&pageSize=1&page=3")).Items);
        Assert.Equal(6, (await ReadAsync(client, "?origin=Manual")).TotalCount);
        Assert.Equal(6, (await ReadAsync(client, "?origin=DirectLink")).TotalCount);
        Assert.Equal(Cases.Length, (await ReadAsync(client, "?origin=&paymentStatus=")).TotalCount);
        Assert.Equal(Cases.Length, (await ReadAsync(client, "?origin=%20&paymentStatus=%20")).TotalCount);
    }

    [Theory]
    [InlineData("origin=WhatsApp")]
    [InlineData("origin=manual")]
    [InlineData("paymentStatus=Cancelled")]
    [InlineData("paymentStatus=Paid")]
    [InlineData("paymentStatus=received")]
    public async Task InvalidFiltersAreRejected(string query)
    {
        using var client = await SignInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?" + query)).StatusCode);
    }
}
