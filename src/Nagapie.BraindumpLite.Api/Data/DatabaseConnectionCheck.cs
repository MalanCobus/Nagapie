using Microsoft.Data.SqlClient;

namespace Nagapie.BraindumpLite.Api.Data;

public static class DatabaseConnectionCheck
{
    public static async Task<int> RunAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var connection = new SqlConnectionStringBuilder(configuration.GetConnectionString("Nagapie"));
            var expectedServer = configuration["Database:ExpectedServer"];
            var server = connection.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split(',')[0];
            if (string.IsNullOrWhiteSpace(expectedServer) || !server.Equals(expectedServer, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(connection.InitialCatalog))
            {
                logger.LogError("Deployment connection must name a database on the configured SQL server.");
                return 1;
            }
            connection.ConnectTimeout = 15;
            // Firewall updates can take several minutes to propagate. Retry only
            // firewall rejection, never authentication, validation or migration errors.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var sql = new SqlConnection(connection.ConnectionString);
                    await sql.OpenAsync(cancellationToken);
                    await using var command = sql.CreateCommand();
                    command.CommandText = "SELECT 1";
                    await command.ExecuteScalarAsync(cancellationToken);
                    logger.LogInformation("Deployment database connection verified.");
                    return 0;
                }
                catch (SqlException exception) when (exception.Number == 40615 && attempt < 20)
                {
                    logger.LogInformation("Waiting for SQL firewall propagation ({Attempt}/20).", attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                }
            }
        }
        catch (Exception exception)
        {
            SafeExceptionLog.Write(logger, exception, "deployment-connection-check");
            return 1;
        }
    }
}
