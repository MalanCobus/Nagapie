using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Api.Accounts;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api;

public static class UserDataEndpoints
{
    public static void MapUserDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/data").RequireAuthorization()
            .AddEndpointFilter<AccountRequestFilter>();
        group.MapGet("/{key}", ReadAsync);
        group.MapPut("/{key}", SaveAsync);
        group.MapDelete("/{key}", DeleteAsync);
        group.MapPost("/items/changes", async (SaveThoughtsRequest request, IRelationalDataStore store, HttpContext context) =>
            Results.Ok(await store.SaveThoughtsAsync(UserId(context), request, context.RequestAborted)));
        group.MapPost("/categories/changes", async (SaveCategoriesRequest request, IRelationalDataStore store, HttpContext context) =>
            Results.Ok(await store.SaveCategoriesAsync(UserId(context), request, context.RequestAborted)));
        group.MapGet("/history/dumps", async (int? page, HttpContext context, IRelationalDataStore store) =>
        {
            if (page is < 0 or > 100000)
                return Results.BadRequest();
            return Results.Ok(await store.HistoryAsync(UserId(context), page ?? 0, context.RequestAborted));
        });
        group.MapPost("/clear", async (HttpContext context, IRelationalDataStore store) =>
        {
            await store.ClearAsync(UserId(context), context.RequestAborted);
            return Results.NoContent();
        });
    }
    private static async Task<IResult> ReadAsync(string key, IUserDocumentStore store, HttpContext context)
    {
        if (!UserDocumentValidation.Keys.Contains(key))
        {
            return Results.NotFound();
        }

        return Results.Ok(await store.ReadAsync(UserId(context), key, context.RequestAborted));
    }

    private static async Task<IResult> SaveAsync(
        string key, SaveUserDocumentRequest request, IUserDocumentStore store, HttpContext context)
    {
        if (!UserDocumentValidation.Keys.Contains(key))
        {
            return Results.NotFound();
        }

        if (!UserDocumentValidation.IsValid(key, request.Data))
        {
            return Results.BadRequest(new ApiError(ErrorCodes.InvalidInput, context.TraceIdentifier));
        }

        var version = await store.SaveAsync(UserId(context), key, request.Data, request.Version, context.RequestAborted);
        return Saved(version, context);
    }

    private static async Task<IResult> DeleteAsync(
        string key, Guid version, IUserDocumentStore store, HttpContext context)
    {
        if (!UserDocumentValidation.Keys.Contains(key))
        {
            return Results.NotFound();
        }

        var next = await store.SaveAsync(UserId(context), key, null, version, context.RequestAborted);
        return Saved(next, context);
    }

    private static string UserId(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static IResult Saved(Guid? version, HttpContext context) => version is { } next
        ? Results.Ok(new UserDocumentResponse(null, next))
        : Results.Conflict(new ApiError(ErrorCodes.SaveConflict, context.TraceIdentifier));
}
