using Microsoft.Extensions.Options;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Api.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

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

    private static async Task<IResult> ProcessDumpAsync(ProcessDumpRequest request, IAiBrainDumpProcessor ai,
        NagapieDbContext database, LegacyDataImporter importer, IOptions<AccessOptions> access, HttpContext context)
    {
        if (!DumpValidation.IsValid(request))
        {
            return Results.BadRequest(new ApiError(ErrorCodes.InvalidInput, context.TraceIdentifier));
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await importer.EnsureImportedAsync(userId, context.RequestAborted);
        var account = await database.RelationalAccounts.AsNoTracking().SingleAsync(row => row.UserId == userId, context.RequestAborted);
        if (access.Value.PaywallEnabled && account.SuccessfulAiDumps >= AccessOptions.FreeDumpLimit && account.LicenseHash is null)
        {
            return Failure(ErrorCodes.PaywallRequired, StatusCodes.Status402PaymentRequired, context);
        }

        try
        {
            var result = await ai.ProcessAsync(request, context.RequestAborted);
            await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, context.RequestAborted);
            if (!await database.ProcessedDumps.AnyAsync(row => row.UserId == userId && row.Id == request.SourceDumpId, context.RequestAborted))
            {
                database.ProcessedDumps.Add(new()
                {
                    UserId = userId,
                    Id = request.SourceDumpId
                });
                await database.SaveChangesAsync(context.RequestAborted);
            }
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(result);
        }
        catch (AiFailure error)
        {
            return Failure(error.Code, StatusCodes.Status503ServiceUnavailable, context);
        }
    }

    private static async Task<IResult> VerifyLicenseAsync(VerifyLicenseRequest request, ILicenseVerifier verifier,
        NagapieDbContext database, LegacyDataImporter importer, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || request.LicenseKey.Length > 100)
        {
            return Results.BadRequest(new ApiError(ErrorCodes.LicenseInvalid, context.TraceIdentifier));
        }

        try
        {
            var verified = await verifier.VerifyAsync(request.LicenseKey, context.RequestAborted);
            if (!verified.IsValid)
                return Results.Ok(verified);
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await importer.EnsureImportedAsync(userId, context.RequestAborted);
            await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, context.RequestAborted);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.LicenseKey.Trim())));
            if (await database.RelationalAccounts.AnyAsync(row => row.UserId != userId && row.LicenseHash == hash, context.RequestAborted))
                return Results.BadRequest(new ApiError(ErrorCodes.LicenseInvalid, context.TraceIdentifier));
            (await database.RelationalAccounts.SingleAsync(row => row.UserId == userId, context.RequestAborted)).LicenseHash = hash;
            await database.SaveChangesAsync(context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            // The browser marker controls presentation only; authorization uses the account record.
            return Results.Ok(new VerifyLicenseResponse(true, "account-entitlement"));
        }
        catch (LicenseUnavailableException)
        {
            return Failure(ErrorCodes.LicenseUnavailable, StatusCodes.Status503ServiceUnavailable, context);
        }
    }

    private static IResult Failure(string code, int statusCode, HttpContext context) => Results.Json(new ApiError(code, context.TraceIdentifier), statusCode: statusCode);
}
