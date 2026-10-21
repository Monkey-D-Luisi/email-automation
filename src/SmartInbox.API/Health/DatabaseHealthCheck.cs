using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.API.Health;

public sealed class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is unavailable.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Database is unavailable.");
        }
    }
}
