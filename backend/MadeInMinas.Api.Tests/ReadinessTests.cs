using System.Net;
using System.Net.Http.Json;
using MadeInMinas.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class ReadinessTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>
{
    [Fact]
    public async Task StatusIdentifiesBuildAndReadinessRequiresAllMigrations()
    {
        using var client = factory.CreateStaffClient();
        var status = await client.GetFromJsonAsync<SystemStatusResponse>("/api/system/status");
        Assert.Equal("available", status!.Status);
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
        Assert.Equal("Testing", status.Environment);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        await factory.WithDatabaseAsync(async database =>
        {
            const string migration = "20261010152627_AddAiChatAgent";
            var version = await database.Database.SqlQueryRaw<string>(
                "SELECT \"ProductVersion\" AS \"Value\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261010152627_AddAiChatAgent'").SingleAsync();
            try
            {
                await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {migration}");
                var response = await client.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.DoesNotContain("MigrationId", await response.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            }
            finally
            {
                await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migration}, {version})");
            }
        });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }
}
