namespace Nagapie.BraindumpLite.Api.Data;

public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
