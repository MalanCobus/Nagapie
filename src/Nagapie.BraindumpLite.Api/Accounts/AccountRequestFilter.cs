using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api.Accounts;

// JSON APIs using cookies need CSRF protection too, not only HTML form endpoints.
public sealed class AccountRequestFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        if (context.User.Identity?.IsAuthenticated == true &&
            context.Request.Headers["X-Account-Id"] != context.User.FindFirstValue(ClaimTypes.NameIdentifier))
        {
            return Results.Json(new ApiError(ErrorCodes.SessionChanged, context.TraceIdentifier), statusCode: 401);
        }

        if (!HttpMethods.IsGet(context.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Json(new ApiError(ErrorCodes.SessionChanged, context.TraceIdentifier), statusCode: 403);
            }
        }

        return await next(invocation);
    }
}

