using Microsoft.Extensions.Logging;
using Nagapie.BraindumpLite.Api;

namespace Nagapie.BraindumpLite.Tests;

public class SafeLoggingTests
{
    [Fact]
    public void SafeDetailsIncludeExceptionTypesButNeverMessagesOrData()
    {
        var logger = new CaptureLogger();
        var error = new InvalidOperationException("Password=secret; thought content", new IOException("connection-string"));
        error.Data["password"] = "more-secret";
        SafeExceptionLog.Write(logger, error, "correlation-123");
        Assert.Contains("InvalidOperationException", logger.Text);
        Assert.Contains("IOException", logger.Text);
        Assert.Contains("HRESULT=", logger.Text);
        Assert.Contains("correlation-123", logger.Text);
        Assert.DoesNotContain("secret", logger.Text);
        Assert.DoesNotContain("thought content", logger.Text);
        Assert.DoesNotContain("connection-string", logger.Text);
        Assert.Null(logger.Exception);
    }

    private sealed class CaptureLogger : ILogger
    {
        public string Text = "";
        public Exception? Exception;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Text = formatter(state, exception);
            Exception = exception;
        }
    }
}
