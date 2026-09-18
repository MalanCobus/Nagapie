using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public sealed class ApiRequestMiddleware(RequestDelegate next, ILogger<ApiRequestMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ex.StatusCode;
                await context.Response.WriteAsJsonAsync(new ApiError(ErrorCodes.InvalidInput, context.TraceIdentifier));
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new ApiError(ErrorCodes.UnexpectedError, context.TraceIdentifier));
            }

            logger.LogWarning("Request failed {CorrelationId}", context.TraceIdentifier);
        }
        finally
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                logger.LogInformation("API {Path} status {Status} duration {ElapsedMs}ms correlation {CorrelationId}", context.Request.Path.Value, context.Response.StatusCode, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
            }
        }
    }
}
