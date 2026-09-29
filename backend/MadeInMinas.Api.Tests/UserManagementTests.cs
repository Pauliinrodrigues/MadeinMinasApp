using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Users;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class UserManagementTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.WithDatabaseAsync(database => database.Users.ExecuteDeleteAsync());
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> SignInAsync(User user, string? password = null)
    {
        var client = factory.CreateStaffClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username, password ?? factory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private CreateUserRequest NewUser(int roleId = 2) =>
        new("Funcionário teste", "new_" + Guid.NewGuid().ToString("N"), factory.Password, roleId);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorsCanManageUsers(int roleId)
    {
        var target = await factory.CreateUserAsync(2);
        using var client = roleId == 0 ? factory.CreateStaffClient() : await SignInAsync(await factory.CreateUserAsync(roleId));
        var expected = roleId == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(expected, (await client.GetAsync($"/api/users/{target.Id}")).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/users", NewUser())).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/users/{target.Id}",
            new UpdateUserRequest("Changed", target.Username, 3, true))).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/users/{target.Id}/status",
            new UserStatusRequest(false))).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/users/{target.Id}/password",
            new ResetPasswordRequest(factory.Password))).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateAndReadUserWithoutSensitiveFields()
    {
        using var client = await SignInAsync(await factory.CreateUserAsync());
        var request = NewUser();
        using var response = await client.PostAsJsonAsync("/api/users", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.Equal(request.Username, created.Username);
        Assert.Equal("Attendant", created.Role);
        using var loaded = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
        var body = await loaded.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failedLogin", body, StringComparison.OrdinalIgnoreCase);
        await factory.WithDatabaseAsync(async database =>
        {
            var user = await database.Users.SingleAsync(user => user.Id == created.Id);
            Assert.NotEqual(request.Password, user.PasswordHash);
            Assert.Equal(request.Username.ToUpperInvariant(), user.NormalizedUsername);
        });
        var account = new User { Username = created.Username };
        using var staff = await SignInAsync(account);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task DuplicateUsernameIsCaseInsensitiveIncludingInactiveUsers()
    {
        using var client = await SignInAsync(await factory.CreateUserAsync());
        var request = NewUser() with { IsActive = false };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users", request)).StatusCode);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/users", request with { Username = request.Username.ToUpperInvariant() }),
            HttpStatusCode.Conflict, "DuplicateUsername");
    }

    [Fact]
    public async Task ConcurrentDuplicateCreatesReturnOneCreatedAndOneConflict()
    {
        using var client = await SignInAsync(await factory.CreateUserAsync());
        var request = NewUser();
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/users", request), client.PostAsJsonAsync("/api/users", request));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await factory.WithDatabaseAsync(async database =>
            Assert.Equal(1, await database.Users.CountAsync(user => user.NormalizedUsername == request.Username.ToUpperInvariant())));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("               ")]
    public async Task InvalidNewPasswordIsRejected(string password)
    {
        using var client = await SignInAsync(await factory.CreateUserAsync());
        var response = await client.PostAsJsonAsync("/api/users", NewUser() with { Password = password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingStatusAndInvalidRoleAreRejectedWithoutChangingUser()
    {
        var admin = await factory.CreateUserAsync();
        var target = await factory.CreateUserAsync(2);
        using var client = await SignInAsync(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/users/{target.Id}",
            new { name = "Changed", username = target.Username, roleId = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/users/{target.Id}/status", new { })).StatusCode);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/users", NewUser(int.MaxValue)),
            HttpStatusCode.BadRequest, "InvalidRole");
        var saved = (await client.GetFromJsonAsync<UserResponse>($"/api/users/{target.Id}"))!;
        Assert.True(saved.IsActive);
        Assert.Equal(target.Name, saved.Name);
    }

    [Fact]
    public async Task UpdatingLoginAndRoleRevokesSessionAndUsesNewLogin()
    {
        var target = await factory.CreateUserAsync(2);
        using var oldSession = await SignInAsync(target);
        using var client = await SignInAsync(await factory.CreateUserAsync());
        var newUsername = "changed_" + Guid.NewGuid().ToString("N");
        var response = await client.PutAsJsonAsync($"/api/users/{target.Id}",
            new UpdateUserRequest("  Cozinha  ", newUsername, 3, true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.Equal("Cozinha", saved.Name);
        Assert.Equal("Kitchen", saved.Role);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/auth/me")).StatusCode);
        using var newSession = await SignInAsync(new User { Username = newUsername });
        var profile = (await newSession.GetFromJsonAsync<AuthenticatedUserResponse>("/api/auth/me"))!;
        Assert.Equal("Kitchen", profile.Role);
        using var loginClient = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await loginClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(target.Username, factory.Password))).StatusCode);
    }

    [Fact]
    public async Task DuplicateLoginOnUpdatePreservesOriginalUser()
    {
        var first = await factory.CreateUserAsync(2);
        var second = await factory.CreateUserAsync(3);
        using var client = await SignInAsync(await factory.CreateUserAsync());
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/users/{second.Id}",
            new UpdateUserRequest("Changed", first.Username, 3, true)), HttpStatusCode.Conflict, "DuplicateUsername");
        var saved = (await client.GetFromJsonAsync<UserResponse>($"/api/users/{second.Id}"))!;
        Assert.Equal(second.Username, saved.Username);
    }

    [Fact]
    public async Task NameOnlyUpdatePreservesSession()
    {
        var user = await factory.CreateUserAsync(2);
        using var session = await SignInAsync(user);
        using var client = await SignInAsync(await factory.CreateUserAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{user.Id}",
            new UpdateUserRequest("Novo nome", user.Username, 2, true))).StatusCode);
        var profile = (await session.GetFromJsonAsync<AuthenticatedUserResponse>("/api/auth/me"))!;
        Assert.Equal("Novo nome", profile.Name);
    }

    [Fact]
    public async Task DeactivationAndReactivationNeverRestoreAnOldToken()
    {
        var user = await factory.CreateUserAsync(2);
        using var oldSession = await SignInAsync(user);
        using var client = await SignInAsync(await factory.CreateUserAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{user.Id}/status", new UserStatusRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/auth/me")).StatusCode);
        using var loginClient = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await loginClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username, factory.Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{user.Id}/status", new UserStatusRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/auth/me")).StatusCode);
        using var newSession = await SignInAsync(user);
        Assert.Equal(HttpStatusCode.OK, (await newSession.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LastActiveAdministratorCannotBeRemoved(bool deactivate)
    {
        var admin = await factory.CreateUserAsync();
        await factory.CreateUserAsync(active: false);
        using var client = await SignInAsync(admin);
        var response = deactivate
            ? await client.PutAsJsonAsync($"/api/users/{admin.Id}/status", new UserStatusRequest(false))
            : await client.PutAsJsonAsync($"/api/users/{admin.Id}", new UpdateUserRequest(admin.Name, admin.Username, 2, true));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "LastAdministrator");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanDemoteSelfWhenAnotherActiveAdministratorExists()
    {
        var admin = await factory.CreateUserAsync();
        await factory.CreateUserAsync();
        using var client = await SignInAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{admin.Id}",
            new UpdateUserRequest(admin.Name, admin.Username, 2, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        using var newSession = await SignInAsync(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await newSession.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentSelfDeactivationsLeaveAnActiveAdministrator()
    {
        var first = await factory.CreateUserAsync();
        var second = await factory.CreateUserAsync();
        using var firstClient = await SignInAsync(first);
        using var secondClient = await SignInAsync(second);
        var responses = await Task.WhenAll(
            firstClient.PutAsJsonAsync($"/api/users/{first.Id}/status", new UserStatusRequest(false)),
            secondClient.PutAsJsonAsync($"/api/users/{second.Id}/status", new UserStatusRequest(false)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await factory.WithDatabaseAsync(async database =>
            Assert.Equal(1, await database.Users.CountAsync(user => user.IsActive && user.Role.Code == "Administrator")));
    }

    [Fact]
    public async Task RevokedActorCannotPerformAWriteAfterEarlierAuthorization()
    {
        var admin = await factory.CreateUserAsync();
        await factory.WithDatabaseAsync(database => database.Users.Where(user => user.Id == admin.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.SecurityStamp, Guid.NewGuid())));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<UserService>();
        var exception = await Assert.ThrowsAsync<UserManagementException>(() =>
            service.CreateAsync(admin.Id, admin.SecurityStamp, NewUser(), CancellationToken.None));
        Assert.Equal(UserManagementError.InvalidSession, exception.Error);
        await factory.WithDatabaseAsync(async database => Assert.Equal(1, await database.Users.CountAsync()));
    }

    [Fact]
    public async Task ResettingAnotherPasswordRevokesSessionsAndClearsLockout()
    {
        var target = await factory.CreateUserAsync(2);
        using var oldSession = await SignInAsync(target);
        await factory.WithDatabaseAsync(database => database.Users.Where(user => user.Id == target.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.FailedLoginAttempts, 5)
                .SetProperty(user => user.LockoutEndAt, DateTimeOffset.UtcNow.AddMinutes(15))));
        using var admin = await SignInAsync(await factory.CreateUserAsync());
        var newPassword = "Changed password " + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/users/{target.Id}/password",
            new ResetPasswordRequest(newPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/auth/me")).StatusCode);
        using var newSession = await SignInAsync(target, newPassword);
        Assert.Equal(HttpStatusCode.OK, (await newSession.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task StaffCanChangeOwnPasswordAndMustSignInAgain(int roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        using var session = await SignInAsync(user);
        var newPassword = "New own password " + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.NoContent, (await session.PutAsJsonAsync("/api/auth/password",
            new ChangePasswordRequest(factory.Password, newPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        using var anonymous = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username, factory.Password))).StatusCode);
        using var newSession = await SignInAsync(user, newPassword);
        Assert.Equal(HttpStatusCode.OK, (await newSession.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task WrongCurrentPasswordCannotChangePasswordAndTriggersAccountLockout()
    {
        var user = await factory.CreateUserAsync(2);
        using var session = await SignInAsync(user);
        for (var attempt = 0; attempt < 5; attempt++)
            await AssertProblemAsync(await session.PutAsJsonAsync("/api/auth/password",
                new ChangePasswordRequest("wrong", factory.Password)), HttpStatusCode.BadRequest, "InvalidPassword");
        await AssertProblemAsync(await session.PutAsJsonAsync("/api/auth/password",
            new ChangePasswordRequest(factory.Password, factory.Password)), HttpStatusCode.BadRequest, "InvalidPassword");
        await factory.WithDatabaseAsync(async database =>
        {
            var saved = await database.Users.SingleAsync(item => item.Id == user.Id);
            Assert.Equal(5, saved.FailedLoginAttempts);
            Assert.True(saved.LockoutEndAt > DateTimeOffset.UtcNow);
            Assert.Equal(user.SecurityStamp, saved.SecurityStamp);
        });
    }

    [Fact]
    public async Task AdministratorMustUseCurrentPasswordForOwnPasswordChange()
    {
        var admin = await factory.CreateUserAsync();
        using var client = await SignInAsync(admin);
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/users/{admin.Id}/password",
            new ResetPasswordRequest(factory.Password)), HttpStatusCode.BadRequest, "UseOwnPasswordEndpoint");
    }

    [Fact]
    public async Task ListSupportsSearchFiltersPaginationAndLimits()
    {
        using var client = await SignInAsync(await factory.CreateUserAsync());
        foreach (var request in new[]
        {
            new CreateUserRequest("Equipe", "team_01", factory.Password, 2),
            new CreateUserRequest("Equipe", "team_02", factory.Password, 2),
            new CreateUserRequest("Equipe", "team_03", factory.Password, 2, false),
            new CreateUserRequest("Equipe", "team_04", factory.Password, 3)
        })
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users", request)).StatusCode);
        var page = (await client.GetFromJsonAsync<UserPageResponse>("/api/users?search=TEAM&roleId=2&isActive=true&page=2&pageSize=1"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
        Assert.Equal("team_02", page.Items[0].Username);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/users?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/users?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/users?page=2147483647")).StatusCode);
    }

    [Fact]
    public async Task MissingUserReturnsNotFoundForAllOperations()
    {
        using var client = await SignInAsync(await factory.CreateUserAsync());
        var id = Guid.NewGuid();
        await AssertProblemAsync(await client.GetAsync($"/api/users/{id}"), HttpStatusCode.NotFound, "UserNotFound");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/users/{id}",
            new UpdateUserRequest("Missing", "missing", 2, true)), HttpStatusCode.NotFound, "UserNotFound");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/users/{id}/status",
            new UserStatusRequest(false)), HttpStatusCode.NotFound, "UserNotFound");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/users/{id}/password",
            new ResetPasswordRequest(factory.Password)), HttpStatusCode.NotFound, "UserNotFound");
    }
}
