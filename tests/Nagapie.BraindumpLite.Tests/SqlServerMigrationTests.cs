using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NAGAPIE_TEST_SQL")))
            Skip = "Set NAGAPIE_TEST_SQL to a disposable SQL Server instance; CI supplies one.";
    }
}

public class SqlServerMigrationTests
{
    [SqlServerFact]
    public async Task ProductionMigrationsImportLegacyDataAndRuntimeWorksWithoutDdlPermissions()
    {
        var name = "NagapieReviewTests_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NAGAPIE_TEST_SQL")) { InitialCatalog = name };
        var options = new DbContextOptionsBuilder<NagapieDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new NagapieDbContext(options);
        try
        {
            // Exercise the actual old schema -> latest schema path, never EnsureCreated.
            await database.GetService<IMigrator>().MigrateAsync("20260918135021_InitialAccountsAndUserData");
            await database.Database.ExecuteSqlRawAsync("INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) VALUES ('owner', 'owner', 'OWNER', 1, 0, 0, 0, 0)");
            var legacy = new ItemStore { Items = [new() { Text = "Legacy thought", PlanningHorizon = "next-week" }] };
            var json = JsonSerializer.Serialize(legacy, JsonSerializerOptions.Web);
            await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO UserDocuments (UserId, [Key], Json, Version, UpdatedAtUtc) VALUES ('owner', 'items', {json}, {Guid.NewGuid()}, {DateTimeOffset.UtcNow})");
            var importer = new LegacyDataImporter(database, NullLogger<LegacyDataImporter>.Instance);
            var migrator = new SqlDatabaseMigrator(database, importer, NullLogger<SqlDatabaseMigrator>.Instance);
            await migrator.MigrateAsync();
            await migrator.MigrateAsync();
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            Assert.False(database.Database.HasPendingModelChanges());
            Assert.Equal("later", (await database.Thoughts.SingleAsync()).PlanningHorizon);
            Assert.Equal(json, (await database.UserDocuments.SingleAsync()).Json);
            var epoch = (await database.RelationalAccounts.SingleAsync()).Epoch;

            // Independent contexts issue simultaneous category commands under SQL applocks.
            async Task CreateCategory(string label)
            {
                await using var connection = new NagapieDbContext(options);
                var store = new RelationalDataStore(connection, new(connection, NullLogger<LegacyDataImporter>.Instance), Options.Create(new AccessOptions()));
                await store.SaveCategoriesAsync("owner", new(epoch,
                    [new(new(Guid.NewGuid(), "custom", label, "sage", false), Guid.Empty)], []), default);
            }
            await Task.WhenAll(CreateCategory("First"), CreateCategory("Second"));
            Assert.Equal(2, await database.Categories.CountAsync(category => !category.IsDefault));

            await database.Database.OpenConnectionAsync();
            await database.Database.ExecuteSqlRawAsync("CREATE USER [nagapie_runtime_test] WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER [nagapie_runtime_test]; ALTER ROLE db_datawriter ADD MEMBER [nagapie_runtime_test];");
            await database.Database.ExecuteSqlRawAsync("EXECUTE AS USER = 'nagapie_runtime_test'");
            try
            {
                database.ChangeTracker.Clear();
                var store = new RelationalDataStore(database, importer, Options.Create(new AccessOptions()));
                var page = await store.QueryThoughtsAsync("owner", new(), default);
                Assert.Single(page.Items);
                var item = page.Items[0] with
                {
                    Text = "Runtime edit"
                };
                var request = new SaveThoughtsRequest(page.Epoch, [new(item, page.RowVersions[item.Id])], [], OperationId: Guid.NewGuid());
                var saved = await store.SaveThoughtsAsync("owner", request, default);
                database.ChangeTracker.Clear();
                var retried = await store.SaveThoughtsAsync("owner", request, default);
                Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(retried));
                await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync("CREATE TABLE MustNotBeAllowed (Id int)"));
            }
            finally { await database.Database.ExecuteSqlRawAsync("REVERT"); }
        }
        finally
        {
            // Only this test's newly generated database is removed.
            await database.Database.CloseConnectionAsync();
            SqlConnection.ClearAllPools();
            await database.Database.EnsureDeletedAsync();
        }
    }
}
