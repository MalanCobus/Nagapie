using Microsoft.Extensions.Options;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public static class NagapieEndpoints
{
    public static IEndpointRouteBuilder MapNagapieEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
        endpoints.MapGet("/api/config", GetConfiguration);
        endpoints.MapPost("/api/dumps/process", ProcessDumpAsync).RequireRateLimiting("api")
            .RequireAuthorization().AddEndpointFilter<Nagapie.BraindumpLite.Api.Accounts.AccountRequestFilter>();
        endpoints.MapPost("/api/licenses/verify", VerifyLicenseAsync).RequireRateLimiting("api")
            .RequireAuthorization().AddEndpointFilter<Nagapie.BraindumpLite.Api.Accounts.AccountRequestFilter>();
        endpoints.Map("/api/{**path}", () => Results.NotFound());
        return endpoints;
    }

    private static PublicConfiguration GetConfiguration(IOptions<AccessOptions> access, IOptions<AiProviderOptions> ai, IOptions<PayhipOptions> payhip)
    {
        return new PublicConfiguration(access.Value.PaywallEnabled, AccessOptions.FreeDumpLimit, AccessOptions.PriceDisplay, payhip.Value.ProductUrl, ai.Value.IsConfigured, ai.Value.DisplayName, ai.Value.PrivacyUrl);
    }

    private static async Task<IResult> ProcessDumpAsync(ProcessDumpRequest request, IAiBrainDumpProcessor ai, IUnlockTokenService tokens, IOptions<AccessOptions> access, HttpContext context)
    {
        if (!DumpValidation.IsValid(request))
        {
            return Results.BadRequest(new ApiError(ErrorCodes.InvalidInput, context.TraceIdentifier));
        }

        // Accountless trial counts come from local storage and are deliberately a soft limit.
        if (access.Value.PaywallEnabled && request.SuccessfulDumpCount >= AccessOptions.FreeDumpLimit && !tokens.Verify(request.UnlockToken))
        {
            return Failure(ErrorCodes.PaywallRequired, StatusCodes.Status402PaymentRequired, context);
        }

        try
        {
            return Results.Ok(await ai.ProcessAsync(request, context.RequestAborted));
        }
        catch (AiFailure error)
        {
            return Failure(error.Code, StatusCodes.Status503ServiceUnavailable, context);
        }
    }

    private static async Task<IResult> VerifyLicenseAsync(VerifyLicenseRequest request, ILicenseVerifier verifier, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || request.LicenseKey.Length > 100)
        {
            return Results.BadRequest(new ApiError(ErrorCodes.LicenseInvalid, context.TraceIdentifier));
        }

        try
        {
            return Results.Ok(await verifier.VerifyAsync(request.LicenseKey, context.RequestAborted));
        }
        catch (LicenseUnavailableException)
        {
            return Failure(ErrorCodes.LicenseUnavailable, StatusCodes.Status503ServiceUnavailable, context);
        }
    }

    private static IResult Failure(string code, int statusCode, HttpContext context) => Results.Json(new ApiError(code, context.TraceIdentifier), statusCode: statusCode);
}
