using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Customers;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class CustomerTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.Addresses.ExecuteDeleteAsync();
        await database.Customers.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });
    public Task DisposeAsync() => Task.CompletedTask;
    private static CustomerRequest Input(string phone = "(31) 99999-1234", string name = "Maria") => new(name, phone);
    private static AddressRequest AddressInput() => new(" Rua das Flores ", " S/N ", " Centro ", " Belo Horizonte ", "mg",
        Complement: " Casa ", PostalCode: "30110-000", Reference: " Portão verde ");

    private async Task<HttpClient> SignInAsync(int roleId = 1)
    {
        var user = await factory.CreateUserAsync(roleId);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        if (roleId is 1 or 2)
            Assert.Contains("customers.manage", login.User.Permissions);
        else
            Assert.DoesNotContain("customers.manage", login.User.Permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<CustomerResponse> CreateAsync(HttpClient client, CustomerRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/customers", request ?? Input());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var customer = (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
        Assert.Equal(customer, await client.GetFromJsonAsync<CustomerResponse>(response.Headers.Location));
        return customer;
    }

    private static async Task<AddressResponse> CreateAddressAsync(HttpClient client, Guid customerId, AddressRequest? request = null)
    {
        var response = await client.PostAsJsonAsync($"/api/customers/{customerId}/addresses", request ?? AddressInput());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var address = (await response.Content.ReadFromJsonAsync<AddressResponse>())!;
        Assert.Equal(address, await client.GetFromJsonAsync<AddressResponse>(response.Headers.Location));
        return address;
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
    [InlineData(3)]
    [InlineData(4)]
    public async Task AnonymousKitchenAndDispatchCannotAccessAnyOperation(int roleId)
    {
        using var client = roleId == 0 ? factory.CreateStaffClient() : await SignInAsync(roleId);
        var status = roleId == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var customer = $"/api/customers/{Guid.NewGuid()}";
        var address = customer + $"/addresses/{Guid.NewGuid()}";
        Assert.Equal(status, (await client.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(status, (await client.GetAsync(customer)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync("/api/customers", Input())).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(customer, Input())).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(customer + "/status", new CustomerStatusRequest(false))).StatusCode);
        Assert.Equal(status, (await client.GetAsync(customer + "/addresses")).StatusCode);
        Assert.Equal(status, (await client.GetAsync(address)).StatusCode);
        Assert.Equal(status, (await client.PostAsJsonAsync(customer + "/addresses", AddressInput())).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(address, AddressInput())).StatusCode);
        Assert.Equal(status, (await client.PutAsJsonAsync(address + "/status", new CustomerStatusRequest(false))).StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AdministratorAndAttendantCanMaintainCustomersAndAddresses(int roleId)
    {
        using var client = await SignInAsync(roleId);
        var customer = await CreateAsync(client, Input(name: "  Maria  "));
        Assert.Equal("Maria", customer.Name);
        Assert.Equal("+5531999991234", customer.Phone);
        var address = await CreateAddressAsync(client, customer.Id);
        Assert.Equal("Rua das Flores", address.Street);
        Assert.Equal("S/N", address.Number);
        Assert.Equal("Centro", address.Neighborhood);
        Assert.Equal("Belo Horizonte", address.City);
        Assert.Equal("MG", address.State);
        Assert.Equal("30110000", address.PostalCode);
        Assert.Equal("Portão verde", address.Reference);
        var path = $"/api/customers/{customer.Id}";
        var edit = await client.PutAsJsonAsync(path, Input("31 3333-4444", "Maria Silva"));
        edit.EnsureSuccessStatusCode();
        var updated = (await edit.Content.ReadFromJsonAsync<CustomerResponse>())!;
        Assert.Equal(customer.Id, updated.Id);
        Assert.Equal(customer.CreatedAt, updated.CreatedAt);
        Assert.Equal("+553133334444", updated.Phone);
        Assert.Equal(updated, await client.GetFromJsonAsync<CustomerResponse>(path));
        var addressPath = path + $"/addresses/{address.Id}";
        var addressEdit = await client.PutAsJsonAsync(addressPath, AddressInput() with { Number = "12A", Complement = " ", Reference = null, PostalCode = null });
        addressEdit.EnsureSuccessStatusCode();
        var updatedAddress = (await addressEdit.Content.ReadFromJsonAsync<AddressResponse>())!;
        Assert.Equal(address.CreatedAt, updatedAddress.CreatedAt);
        Assert.Equal(customer.Id, updatedAddress.CustomerId);
        Assert.Equal("12A", updatedAddress.Number);
        Assert.Null(updatedAddress.Complement);
        Assert.Null(updatedAddress.PostalCode);
        Assert.Null(updatedAddress.Reference);
        Assert.Equal(updatedAddress, await client.GetFromJsonAsync<AddressResponse>(addressPath));
        foreach (var target in new[] { path, addressPath })
        {
            var inactive = await client.PutAsJsonAsync(target + "/status", new CustomerStatusRequest(false));
            inactive.EnsureSuccessStatusCode();
            using var state = JsonDocument.Parse(await inactive.Content.ReadAsStringAsync());
            Assert.False(state.RootElement.GetProperty("isActive").GetBoolean());
            var repeated = await client.PutAsJsonAsync(target + "/status", new CustomerStatusRequest(false));
            Assert.Equal(await inactive.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
            (await client.PutAsJsonAsync(target + "/status", new CustomerStatusRequest(true))).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(target)).StatusCode);
        }
    }

    [Theory]
    [InlineData("31999991234", "+5531999991234")]
    [InlineData("+55 (31) 99999-1234", "+5531999991234")]
    [InlineData("5531999991234", "+5531999991234")]
    [InlineData("31.3333.4444", "+553133334444")]
    [InlineData("(55) 99999-1234", "+5555999991234")]
    public async Task PhoneFormatsAreNormalizedWithoutAddingMissingDigits(string phone, string expected)
    {
        using var client = await SignInAsync();
        Assert.Equal(expected, (await CreateAsync(client, Input(phone))).Phone);
    }

    [Fact]
    public async Task OnlyNameAndPhoneAreRequiredToCreateCustomer()
    {
        using var client = await SignInAsync();
        var response = await client.PostAsJsonAsync("/api/customers", new { name = "Cliente", phone = "31999991234" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<CustomerResponse>())!.IsActive);
    }

    [Fact]
    public async Task DuplicatePhoneIncludingInactiveConflictsAndRollsBackUpdate()
    {
        using var client = await SignInAsync();
        await CreateAsync(client, Input() with { IsActive = false });
        await ProblemAsync(await client.PostAsJsonAsync("/api/customers", Input("+5531999991234", "Outro cliente")), HttpStatusCode.Conflict, "DuplicateCustomerPhone");
        var other = await CreateAsync(client, Input("31988881234", "Outro"));
        var path = $"/api/customers/{other.Id}";
        await ProblemAsync(await client.PutAsJsonAsync(path, Input(name: "Nome alterado")), HttpStatusCode.Conflict, "DuplicateCustomerPhone");
        Assert.Equal(other, await client.GetFromJsonAsync<CustomerResponse>(path));
    }

    [Fact]
    public async Task ConcurrentDuplicatePhoneCreatesHaveOneWinner()
    {
        using var first = await SignInAsync();
        using var second = await SignInAsync(2);
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/customers", Input()), second.PostAsJsonAsync("/api/customers", Input("+55 (31) 99999-1234")));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        await ProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "DuplicateCustomerPhone");
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Customers.CountAsync()));
    }

    [Fact]
    public async Task SearchPaginationAndStatusAcceptNamesAndFormattedPhones()
    {
        using var client = await SignInAsync();
        var a = await CreateAsync(client, Input("31999990001", "Ana") with { IsActive = false });
        var b = await CreateAsync(client, Input("31999990002", "Bruno"));
        var c = await CreateAsync(client, Input("31999990003", "Carlos"));
        var page = (await client.GetFromJsonAsync<CustomerPageResponse>("/api/customers?pageSize=2"))!;
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { a.Id, b.Id }, page.Items.Select(customer => customer.Id));
        Assert.Equal(c.Id, Assert.Single((await client.GetFromJsonAsync<CustomerPageResponse>("/api/customers?pageSize=2&page=2"))!.Items).Id);
        Assert.Equal(a.Id, Assert.Single((await client.GetFromJsonAsync<CustomerPageResponse>("/api/customers?isActive=false&search=ANA"))!.Items).Id);
        var phoneQuery = Uri.EscapeDataString("+55 (31) 99999-0002");
        Assert.Equal(b.Id, Assert.Single((await client.GetFromJsonAsync<CustomerPageResponse>("/api/customers?search=" + phoneQuery))!.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<CustomerPageResponse>("/api/customers?search=inexistente"))!.Items);
    }

    [Fact]
    public async Task InvalidCustomerFieldsAndPhonesDoNotWrite()
    {
        using var client = await SignInAsync();
        var customer = await CreateAsync(client);
        foreach (var phone in new[] { "", " ", "999991234", "3199991234", "031999991234", "+131999991234", "+31999991234", "31999991234 ramal 1", "31/99999/1234", "３１９９９９９１２３４", "00 99999-1234", "31 1999-1234", "31 89999-1234" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customers", Input(phone))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/customers/{customer.Id}", Input(phone))).StatusCode);
        }
        foreach (var name in new[] { " ", new string('x', 121) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customers", Input(name: name))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customers", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/customers/{customer.Id}/status", new { })).StatusCode);
        Assert.Equal(customer, await client.GetFromJsonAsync<CustomerResponse>($"/api/customers/{customer.Id}"));
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Customers.CountAsync()));
    }

    [Fact]
    public async Task AddressOwnershipIsCheckedOnReadUpdateAndStatus()
    {
        using var client = await SignInAsync();
        var first = await CreateAsync(client);
        var second = await CreateAsync(client, Input("31988881234"));
        var address = await CreateAddressAsync(client, first.Id);
        var wrongPath = $"/api/customers/{second.Id}/addresses/{address.Id}";
        await ProblemAsync(await client.GetAsync(wrongPath), HttpStatusCode.NotFound, "AddressNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(wrongPath, AddressInput()), HttpStatusCode.NotFound, "AddressNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(wrongPath + "/status", new CustomerStatusRequest(false)), HttpStatusCode.NotFound, "AddressNotFound");
        Assert.Empty((await client.GetFromJsonAsync<AddressPageResponse>($"/api/customers/{second.Id}/addresses"))!.Items);
        Assert.Equal(address, await client.GetFromJsonAsync<AddressResponse>($"/api/customers/{first.Id}/addresses/{address.Id}"));
    }

    [Fact]
    public async Task AddressPaginationAndCustomerInactivationPreserveAddresses()
    {
        using var client = await SignInAsync();
        var customer = await CreateAsync(client);
        var first = await CreateAddressAsync(client, customer.Id);
        var second = await CreateAddressAsync(client, customer.Id, AddressInput() with { IsActive = false, Street = "Outra rua" });
        var root = $"/api/customers/{customer.Id}";
        (await client.PutAsJsonAsync(root + "/status", new CustomerStatusRequest(false))).EnsureSuccessStatusCode();
        var page = (await client.GetFromJsonAsync<AddressPageResponse>(root + "/addresses?pageSize=1"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal(second.Id, Assert.Single((await client.GetFromJsonAsync<AddressPageResponse>(root + "/addresses?pageSize=1&page=2"))!.Items).Id);
        Assert.Equal(first.Id, Assert.Single((await client.GetFromJsonAsync<AddressPageResponse>(root + "/addresses?isActive=true"))!.Items).Id);
        Assert.Equal(first, await client.GetFromJsonAsync<AddressResponse>(root + $"/addresses/{first.Id}"));
        await CreateAddressAsync(client, customer.Id, AddressInput() with { Number = "14" });
    }

    [Fact]
    public async Task InvalidAddressFieldsDoNotWriteOrModifyExistingAddress()
    {
        using var client = await SignInAsync();
        var customer = await CreateAsync(client);
        var address = await CreateAddressAsync(client, customer.Id);
        var root = $"/api/customers/{customer.Id}/addresses";
        AddressRequest[] invalid = [
            AddressInput() with { Street = " " }, AddressInput() with { Number = " " },
            AddressInput() with { Neighborhood = "" }, AddressInput() with { City = "" },
            AddressInput() with { State = "XX" }, AddressInput() with { State = "" },
            AddressInput() with { PostalCode = "30110" }, AddressInput() with { PostalCode = "abcdefgh" },
            AddressInput() with { Street = new string('x', 121) }, AddressInput() with { Number = new string('x', 21) },
            AddressInput() with { Complement = new string('x', 121) }, AddressInput() with { Neighborhood = new string('x', 81) },
            AddressInput() with { City = new string('x', 81) }, AddressInput() with { Reference = new string('x', 251) }
        ];
        foreach (var input in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(root, input)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(root + "/" + address.Id, input)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(root, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(root + $"/{address.Id}/status", new { })).StatusCode);
        Assert.Equal(address, await client.GetFromJsonAsync<AddressResponse>(root + "/" + address.Id));
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Addresses.CountAsync()));
    }

    [Fact]
    public async Task MissingCustomerIsNotReportedAsAnEmptyAddressList()
    {
        using var client = await SignInAsync();
        var path = $"/api/customers/{Guid.NewGuid()}";
        await ProblemAsync(await client.GetAsync(path), HttpStatusCode.NotFound, "CustomerNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(path, Input()), HttpStatusCode.NotFound, "CustomerNotFound");
        await ProblemAsync(await client.PutAsJsonAsync(path + "/status", new CustomerStatusRequest(false)), HttpStatusCode.NotFound, "CustomerNotFound");
        await ProblemAsync(await client.GetAsync(path + "/addresses"), HttpStatusCode.NotFound, "CustomerNotFound");
        await ProblemAsync(await client.PostAsJsonAsync(path + "/addresses", AddressInput()), HttpStatusCode.NotFound, "CustomerNotFound");
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=1000001")]
    [InlineData("?isActive=invalid")]
    public async Task InvalidPaginationIsRejectedForCustomersAndAddresses(string query)
    {
        using var client = await SignInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/customers" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/customers/{Guid.NewGuid()}/addresses" + query)).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EveryWriteRechecksSessionAndCurrentPermission(bool revoke)
    {
        var actor = await factory.CreateUserAsync(2);
        await factory.WithDatabaseAsync(async database =>
        {
            var user = await database.Users.SingleAsync(user => user.Id == actor.Id);
            if (revoke)
                user.SecurityStamp = Guid.NewGuid();
            else
                user.RoleId = 3;
            await database.SaveChangesAsync();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var id = Guid.NewGuid();
        Func<Task>[] operations = [
            () => service.CreateAsync(actor.Id, actor.SecurityStamp, Input(), CancellationToken.None),
            () => service.UpdateAsync(actor.Id, actor.SecurityStamp, id, Input(), CancellationToken.None),
            () => service.SetStatusAsync(actor.Id, actor.SecurityStamp, id, false, CancellationToken.None),
            () => service.CreateAddressAsync(actor.Id, actor.SecurityStamp, id, AddressInput(), CancellationToken.None),
            () => service.UpdateAddressAsync(actor.Id, actor.SecurityStamp, id, id, AddressInput(), CancellationToken.None),
            () => service.SetAddressStatusAsync(actor.Id, actor.SecurityStamp, id, id, false, CancellationToken.None)
        ];
        foreach (var operation in operations)
        {
            var failure = await Assert.ThrowsAsync<CustomerException>(operation);
            Assert.Equal(revoke ? CustomerError.InvalidSession : CustomerError.PermissionDenied, failure.Error);
        }
        await factory.WithDatabaseAsync(async database => Assert.Empty(await database.Customers.ToListAsync()));
    }

    [Fact]
    public async Task RevokedJwtCannotReadCustomerData()
    {
        using var client = await SignInAsync(2);
        var customer = await CreateAsync(client);
        await factory.WithDatabaseAsync(async database => await database.Users.ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/customers/{customer.Id}/addresses")).StatusCode);
    }
}
