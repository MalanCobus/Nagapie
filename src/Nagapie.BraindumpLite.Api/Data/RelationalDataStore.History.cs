using System.Data;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed partial class RelationalDataStore
{
    public async Task ClearAsync(string userId, CancellationToken cancellationToken)
    {
        await importer.EnsureImportedAsync(userId, cancellationToken);
        await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, cancellationToken);
        var account = await database.RelationalAccounts.SingleAsync(row => row.UserId == userId, cancellationToken);
        account.Epoch = Guid.NewGuid();
        await database.OperationReceipts.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.ProcessedDumps.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.Thoughts.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.Categories.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.BrainDumps.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        database.Categories.AddRange(CategoryRules.CreateDefaults().Select(category => category.ToRow(userId)));
        var draft = await database.Drafts.SingleAsync(row => row.UserId == userId, cancellationToken);
        draft.IsDeleted = true;
        draft.Text = "";
        draft.ReviewJson = null;
        draft.Version = Guid.NewGuid();
        var documents = await database.UserDocuments.Where(row => row.UserId == userId).ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            // Clear the recovery archive too; delete-all must really remove the user's saved content.
            document.Json = null;
            document.Version = Guid.NewGuid();
            document.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<SavedDumpResponse>> HistoryAsync(string userId, int page, CancellationToken cancellationToken)
    {
        await importer.EnsureImportedAsync(userId, cancellationToken);
        return await database.BrainDumps.AsNoTracking().Where(row => row.UserId == userId && row.OriginalAvailable)
            .OrderByDescending(row => row.SavedAtUtc).ThenByDescending(row => row.Id)
            .Skip(page * 50).Take(50)
            .Select(row => new SavedDumpResponse(row.Id, row.Text, row.InputMethod, row.SavedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
