using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed partial class RelationalDataStore
{
    public async Task<ThoughtPage> QueryThoughtsAsync(string userId, ThoughtQuery query, CancellationToken cancellationToken)
    {
        if (query.Page is < 0 or > 100000 || query.PageSize is < 1 or > 100 ||
            query.CategoryId is not null && query.Unsorted ||
            query.Horizon is not null && !PlanningHorizons.All.Contains(query.Horizon))
            throw new InvalidDataException();
        await importer.EnsureImportedAsync(userId, cancellationToken);
        // Optimistic reset check avoids a serializable read transaction and its range locks.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var account = await database.RelationalAccounts.AsNoTracking().SingleAsync(row => row.UserId == userId, cancellationToken);
            var rows = database.Thoughts.AsNoTracking().Where(row => row.UserId == userId);
            if (query.CategoryId is { } categoryId)
                rows = rows.Where(row => row.CategoryId == categoryId);
            if (query.Unsorted)
                rows = rows.Where(row => row.CategoryId == null);
            if (query.Completed is { } completed)
                rows = rows.Where(row => (row.CompletionReason != null) == completed);
            if (query.Horizon is { } horizon)
                rows = rows.Where(row => row.PlanningHorizon == horizon);
            var items = await rows.OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id)
                .Skip(query.Page * query.PageSize).Take(query.PageSize + 1).ToListAsync(cancellationToken);
            var draftId = await database.Drafts.Where(row => row.UserId == userId && !row.IsDeleted)
                .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(cancellationToken);
            var committed = draftId is { } id && await database.BrainDumps.AnyAsync(row => row.UserId == userId && row.Id == id && row.IsCommitted, cancellationToken);
            if (account.Epoch != await database.RelationalAccounts.Where(row => row.UserId == userId).Select(row => row.Epoch).SingleAsync(cancellationToken))
                continue;
            return new(items.Take(query.PageSize).Select(row => row.ToContract()).ToList(), account.Epoch,
                items.Take(query.PageSize).ToDictionary(row => row.Id, row => row.Version), items.Count > query.PageSize,
                account.SuccessfulAiDumps, committed);
        }
        throw new DataConflictException();
    }
}
