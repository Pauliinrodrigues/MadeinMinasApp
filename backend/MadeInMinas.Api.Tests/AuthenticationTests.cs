using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class AuthenticationTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>
{
    private Task<HttpResponseMessage> LoginAsync(HttpClient client, User user, string? password = null) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, password ?? factory.Password));

    private async Task<LoginResponse> SignInAsync(HttpClient client, User user)
    {
        using var response = await LoginAsync(client, user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task ProtectedEndpointsRejectAnonymousRequests()
    {
        using var client = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/system/status")).StatusCode);
    }

    [Fact]
    public async Task LoginIsCaseInsensitiveAndReturnsOnlySafeProfileData()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateStaffClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Username.ToUpperInvariant(), factory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
        var login = JsonSerializer.Deserialize<LoginResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Bearer", login.TokenType);
        Assert.InRange(login.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        Authenticate(client, login.AccessToken);
        var profile = (await client.GetFromJsonAsync<AuthenticatedUserResponse>("/api/auth/me"))!;
        Assert.Equal(user.Id, profile.Id);
        Assert.Contains(AccessPolicies.ManageUsers, profile.Permissions);
        var roles = await client.GetAsync("/api/roles");
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Equal(4, JsonDocument.Parse(await roles.Content.ReadAsStringAsync()).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task InvalidUnknownAndInactiveAccountsHaveSameFailure()
    {
        var active = await factory.CreateUserAsync();
        var inactive = await factory.CreateUserAsync(active: false);
        var unknown = new User { Username = "missing_" + Guid.NewGuid().ToString("N") };
        using var client = factory.CreateStaffClient();
        string? title = null;
        foreach (var (user, password) in new[] { (active, "wrong password"), (inactive, factory.Password), (unknown, factory.Password) })
        {
            using var response = await LoginAsync(client, user, password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var currentTitle = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString();
            title ??= currentTitle;
            Assert.Equal(title, currentTitle);
        }
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("x", "password")]
    [InlineData("invalid login", "password")]
    [InlineData("staff", "")]
    public async Task InvalidLoginInputIsRejected(string username, string password)
    {
        using var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FiveFailuresLockAccountAndExpiredLockCanRecover()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateStaffClient();
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user, "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user)).StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            var saved = await database.Users.SingleAsync(item => item.Id == user.Id);
            Assert.Equal(5, saved.FailedLoginAttempts);
            Assert.True(saved.LockoutEndAt > DateTimeOffset.UtcNow);
            saved.LockoutEndAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await database.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user)).StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            var saved = await database.Users.SingleAsync(item => item.Id == user.Id);
            Assert.Equal(0, saved.FailedLoginAttempts);
            Assert.Null(saved.LockoutEndAt);
        });
    }

    [Fact]
    public async Task ConcurrentFailuresAreCountedWithoutLostUpdates()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateStaffClient();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => LoginAsync(client, user, "wrong")));
        Assert.All(results, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        await factory.WithDatabaseAsync(async database =>
        {
            var saved = await database.Users.SingleAsync(item => item.Id == user.Id);
            Assert.Equal(5, saved.FailedLoginAttempts);
            Assert.NotNull(saved.LockoutEndAt);
        });
    }

    [Fact]
    public async Task LogoutRevokesAllExistingSessions()
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateStaffClient();
        var first = await SignInAsync(client, user);
        var second = await SignInAsync(client, user);
        Authenticate(client, first.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Authenticate(client, second.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("role")]
    [InlineData("stamp")]
    public async Task ChangedAccountInvalidatesExistingToken(string change)
    {
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateStaffClient();
        var login = await SignInAsync(client, user);
        await factory.WithDatabaseAsync(async database =>
        {
            var saved = await database.Users.SingleAsync(item => item.Id == user.Id);
            if (change == "inactive")
                saved.IsActive = false;
            if (change == "role")
                saved.RoleId = 3;
            if (change == "stamp")
                saved.SecurityStamp = Guid.NewGuid();
            await database.SaveChangesAsync();
        });
        Authenticate(client, login.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData(2, AccessPolicies.ManageOrders)]
    [InlineData(3, AccessPolicies.WorkKitchen)]
    [InlineData(4, AccessPolicies.WorkDispatch)]
    public async Task OperationalRolesCannotReadAdministrativeEndpoint(int roleId, string permission)
    {
        var user = await factory.CreateUserAsync(roleId);
        using var client = factory.CreateStaffClient();
        var login = await SignInAsync(client, user);
        string[] expectedPermissions = roleId == 2 ? [AccessPolicies.ManageCustomers, permission] : [permission];
        Assert.Equal(expectedPermissions, login.User.Permissions);
        Authenticate(client, login.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("algorithm")]
    [InlineData("stamp")]
    public async Task InvalidTokenIsRejected(string kind)
    {
        var user = await factory.CreateUserAsync();
        var claims = new List<Claim> { new("sub", user.Id.ToString()), new("role", "Administrator") };
        if (kind != "stamp")
            claims.Add(new Claim("auth_stamp", user.SecurityStamp.ToString()));
        var key = kind == "signature" ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(factory.SigningKey);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            kind == "issuer" ? "untrusted" : "MadeInMinas.Tests",
            kind == "audience" ? "untrusted" : "MadeInMinas.Tests.Staff",
            claims, now.AddMinutes(-30), kind == "expired" ? now.AddMinutes(-1) : now.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(key),
                kind == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256));
        using var client = factory.CreateStaffClient();
        Authenticate(client, new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task LoginRateLimitRejectsEleventhAttempt()
    {
        using var client = factory.CreateStaffClient();
        var user = new User { Username = "unknown_" + Guid.NewGuid().ToString("N") };
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, user)).StatusCode);
    }

    [Fact]
    public async Task BootstrapValidatesPasswordHashesItAndCannotBeRepeated()
    {
        await factory.WithDatabaseAsync(database => database.Users.ExecuteDeleteAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<AdministratorBootstrap>();
        await Assert.ThrowsAsync<ValidationException>(() => bootstrap.CreateAsync("Admin", "admin", "short"));
        await bootstrap.CreateAsync("Admin", "admin", factory.Password);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.CreateAsync("Other", "other", factory.Password));
        await factory.WithDatabaseAsync(async database =>
        {
            var user = await database.Users.SingleAsync();
            Assert.Equal(1, user.RoleId);
            Assert.NotEqual(factory.Password, user.PasswordHash);
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash, factory.Password));
        });
    }
}
