using MadeInMinas.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MadeInMinas.Api.Infrastructure;

public sealed class DatabaseHealthCheck(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            return HealthCheckResult.Unhealthy("Database connection is not configured.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;
        var canConnect = await database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database is unavailable.");
    }
}
