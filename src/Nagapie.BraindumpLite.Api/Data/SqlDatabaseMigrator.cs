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
        catch (Exception)
        {
            logger.LogCritical("Database migration failed. Startup stopped. Check SQL connectivity and schema-change permissions.");
            throw;
        }
    }
}
