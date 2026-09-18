using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Nagapie.BraindumpLite.Api;

public static class SafeExceptionLog
{
    public static void Write(ILogger logger, Exception exception, string correlationId)
    {
        // Never log Exception.Message, Data, SQL text, parameter values or raw exceptions.
        // Type names, numeric codes and method names retain diagnostic value without user data.
        var details = new List<string>();
        for (Exception? error = exception; error is not null && details.Count < 8; error = error.InnerException)
        {
            var codes = error is SqlException sql
                ? string.Join(",", sql.Errors.Cast<SqlError>().Select(value => $"{value.Number}/{value.State}/{value.Class}")) : "";
            var frames = string.Join(" > ", new StackTrace(error, false).GetFrames().Take(12)
                .Select(frame => frame.GetMethod()).Select(method => $"{method?.DeclaringType?.FullName}.{method?.Name}"));
            details.Add($"{error.GetType().FullName} HRESULT={error.HResult} SQL={codes} at {frames}");
        }
        logger.LogError("Request failed {CorrelationId}: {SafeExceptionDetails}", correlationId, string.Join(" | ", details));
    }
}
