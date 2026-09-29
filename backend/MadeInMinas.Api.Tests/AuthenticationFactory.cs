using System.Net;
using System.Security.Cryptography;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MadeInMinas.Api.Tests;

public sealed class AuthenticationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public string Password { get; } = "Test-only phrase " + Guid.NewGuid().ToString("N");
    private int clientNumber;
    private readonly string connectionString = GetTestConnection();

    private static string GetTestConnection()
    {
        var connection = Environment.GetEnvironmentVariable("AuthTests__ConnectionString")
            ?? throw new InvalidOperationException("Execute scripts/Test-Authentication.ps1 para criar o PostgreSQL isolado.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (parsed.Database != "made_in_minas_auth_tests" || parsed.Host != "127.0.0.1" || parsed.Port != 55433)
            throw new InvalidOperationException("Os testes so aceitam o banco isolado em 127.0.0.1:55433.");
        return connection;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Issuer"] = "MadeInMinas.Tests",
                ["Jwt:Audience"] = "MadeInMinas.Tests.Staff",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, TestIpStartupFilter>());
    }

    public HttpClient CreateStaffClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-IP", "127.0.0." + Interlocked.Increment(ref clientNumber));
        return client;
    }

    public async Task InitializeAsync() => await WithDatabaseAsync(async database =>
        await database.Database.MigrateAsync());

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    public async Task WithDatabaseAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<User> CreateUserAsync(int roleId = 1, bool active = true)
    {
        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Name = "Test staff",
            Username = "staff_" + Guid.NewGuid().ToString("N"),
            RoleId = roleId,
            IsActive = active
        };
        user.NormalizedUsername = user.Username.ToUpperInvariant();
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()
            .HashPassword(user, Password);
        database.Users.Add(user);
        await database.SaveChangesAsync();
        return user;
    }

    private sealed class TestIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-IP"], out var address))
                    context.Connection.RemoteIpAddress = address;
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
