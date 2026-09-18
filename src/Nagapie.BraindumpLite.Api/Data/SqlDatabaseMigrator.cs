using Microsoft.EntityFrameworkCore;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed class SqlDatabaseMigrator(NagapieDbContext database, LegacyDataImporter importer, ILogger<SqlDatabaseMigrator> logger)
    : IDatabaseMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Applying pending database migrations.");
        try
        {
            // EF Core acquires the migration lock and tracks already applied migrations.
            await database.Database.MigrateAsync(cancellationToken);
            await importer.ImportAllAsync(cancellationToken);
            logger.LogInformation("Database migrations complete.");
        }
        catch (Exception exception)
        {
            SafeExceptionLog.Write(logger, exception, "database-upgrade");
            logger.LogCritical("Database upgrade failed. Do not promote this deployment. Original legacy documents are retained.");
            throw;
        }
    }
}
