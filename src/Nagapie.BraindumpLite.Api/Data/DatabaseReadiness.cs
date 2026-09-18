using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed class DatabaseReadiness(IServiceScopeFactory scopes, ILogger<DatabaseReadiness> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<NagapieDbContext>();
            if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return HealthCheckResult.Unhealthy("Database upgrade required.");

            // Read through the website identity; never migrate or import during a probe.
            await database.RelationalAccounts.AsNoTracking().Take(1).ToListAsync(cancellationToken);
            await database.OperationReceipts.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
            await database.ProcessedDumps.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            SafeExceptionLog.Write(logger, exception, "database-readiness");
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
