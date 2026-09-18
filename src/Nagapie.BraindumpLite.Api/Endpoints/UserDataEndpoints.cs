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
        group.MapGet("/history/dumps", async (HttpContext context, NagapieDbContext database) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var dumps = await database.BrainDumps.AsNoTracking()
                .Where(dump => dump.UserId == userId)
                .Select(dump => new SavedDumpResponse(dump.Id, dump.Text, dump.InputMethod, dump.SavedAtUtc))
                .ToListAsync(context.RequestAborted);
            return dumps.OrderByDescending(dump => dump.SavedAtUtc);
        });
        group.MapPost("/clear", async (HttpContext context, NagapieDbContext database) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var documents = await database.UserDocuments.Where(document => document.UserId == userId)
                .ToListAsync(context.RequestAborted);
            foreach (var document in documents)
            {
                document.Json = null;
                document.Version = Guid.NewGuid();
            }
            database.BrainDumps.RemoveRange(await database.BrainDumps
                .Where(dump => dump.UserId == userId).ToListAsync(context.RequestAborted));
            await database.SaveChangesAsync(context.RequestAborted);
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
