using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nagapie.BraindumpLite.Api.Data;

namespace Nagapie.BraindumpLite.Tests;

public class DatabaseStartupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartupHonorsMigrationSetting(bool enabled)
    {
        var migrator = new RecordingMigrator();
        using var factory = Factory(migrator, enabled);
        using var client = factory.CreateClient();
        Assert.Equal(enabled ? 1 : 0, migrator.Calls);
    }

    [Fact]
    public void FailedMigrationPreventsApplicationStartup()
    {
        var migrator = new RecordingMigrator { Fail = true };
        using var factory = Factory(migrator, true);
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Equal(1, migrator.Calls);
    }

    private static WebApplicationFactory<Program> Factory(RecordingMigrator migrator, bool enabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Database:ApplyMigrationsOnStartup"] = enabled.ToString() }));
            builder.ConfigureServices(services =>
            {
                AccountTestSupport.UseTestDatabase(services);
                services.RemoveAll<IDatabaseMigrator>();
                services.AddSingleton<IDatabaseMigrator>(migrator);
            });
        });

    private sealed class RecordingMigrator : IDatabaseMigrator
    {
        public int Calls
        {
            get; private set;
        }
        public bool Fail
        {
            get; init;
        }

        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail ? Task.FromException(new InvalidOperationException("Test migration failure.")) : Task.CompletedTask;
        }
    }
}
