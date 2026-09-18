using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nagapie.BraindumpLite.Api.Data;

namespace Nagapie.BraindumpLite.Tests;

public class DatabaseConnectionCheckTests
{
    [Theory]
    [InlineData("Server=wrong.database.windows.net;Database=app", "expected.database.windows.net")]
    [InlineData("Server=expected.database.windows.net", "expected.database.windows.net")]
    [InlineData("Server=expected.database.windows.net;Database=app", null)]
    public async Task RejectsAnUnspecifiedDatabaseOrUnexpectedServer(string connection, string? expectedServer)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Nagapie"] = connection,
            ["Database:ExpectedServer"] = expectedServer
        }).Build();
        Assert.Equal(1, await DatabaseConnectionCheck.RunAsync(config, NullLogger.Instance, default));
    }
}
