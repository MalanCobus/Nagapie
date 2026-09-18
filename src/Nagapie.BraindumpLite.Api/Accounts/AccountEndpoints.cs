using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api.Accounts;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/account/session", (HttpContext context, IAntiforgery antiforgery) =>
            new AccountSession(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                context.User.FindFirstValue(ClaimTypes.Email),
                antiforgery.GetAndStoreTokens(context).RequestToken!));

        var account = endpoints.MapGroup("/api/account")
            .AddEndpointFilter<AccountRequestFilter>()
            .RequireRateLimiting("api");
        account.MapPost("/register", RegisterAsync);
        account.MapPost("/login", LoginAsync);
        account.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static bool IsValid(CredentialsRequest request) =>
        !string.IsNullOrWhiteSpace(request.Email) &&
        request.Email.Length <= 254 &&
        new EmailAddressAttribute().IsValid(request.Email.Trim()) &&
        request.Password is { Length: >= 12 and <= 128 };

    private static async Task<IResult> RegisterAsync(
        CredentialsRequest request, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, HttpContext context)
    {
        if (!IsValid(request))
        {
            return Failure(ErrorCodes.AccountInput, context);
        }

        var email = request.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Failure(ErrorCodes.RegisterFailed, context);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return Results.NoContent();
    }

    private static async Task<IResult> LoginAsync(
        CredentialsRequest request, SignInManager<ApplicationUser> signIn, HttpContext context)
    {
        if (!IsValid(request))
        {
            return Failure(ErrorCodes.LoginFailed, context);
        }

        var result = await signIn.PasswordSignInAsync(
            request.Email.Trim(), request.Password, isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? Results.NoContent() : Failure(ErrorCodes.LoginFailed, context);
    }

    private static IResult Failure(string code, HttpContext context) =>
        Results.BadRequest(new ApiError(code, context.TraceIdentifier));
}
