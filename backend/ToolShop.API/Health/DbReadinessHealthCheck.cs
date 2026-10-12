using Microsoft.Extensions.Diagnostics.HealthChecks;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Health;

/// <summary>Readiness probe: can the API reach PostgreSQL? Never returns connection details.</summary>
public sealed class DbReadinessHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("database unreachable");
        }
    }
}
