using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public sealed class ApiRequestMiddleware(RequestDelegate next, ILogger<ApiRequestMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
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
        catch (Exception exception) when (exception is DataConflictException or DbUpdateConcurrencyException or
            Microsoft.Data.SqlClient.SqlException { Number: 1205 or 51000 } ||
            exception is DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 or 1205 } })
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ApiError(ErrorCodes.SaveConflict, context.TraceIdentifier));
        }
        catch (TrialLimitException)
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(new ApiError(ErrorCodes.PaywallRequired, context.TraceIdentifier));
        }
        catch (InvalidDataException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ApiError(ErrorCodes.InvalidInput, context.TraceIdentifier));
        }
        catch (Exception exception)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new ApiError(ErrorCodes.UnexpectedError, context.TraceIdentifier));
            }

            SafeExceptionLog.Write(logger, exception, context.TraceIdentifier);
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
