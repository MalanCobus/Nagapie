using System.Data;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed partial class RelationalDataStore
{
    public async Task<UserDocumentResponse> SaveCategoriesAsync(string userId, SaveCategoriesRequest request, CancellationToken cancellationToken)
    {
        if (request.Upserts is null || request.Deletes is null || request.Upserts.Count + request.Deletes.Count > 50 ||
            request.Upserts.Any(change => change?.Value is null) || request.Deletes.Any(change => change is null))
            throw new InvalidDataException();
        var ids = request.Upserts.Select(change => change.Value.Id).Concat(request.Deletes.Select(change => change.Id)).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidDataException();
        await importer.EnsureImportedAsync(userId, cancellationToken);
        await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, cancellationToken);
        await RequireEpochAsync(userId, request.Epoch, cancellationToken);
        var rows = await database.Categories.Where(row => row.UserId == userId).ToDictionaryAsync(row => row.Id, cancellationToken);
        var versions = new Dictionary<Guid, Guid>();
        foreach (var change in request.Upserts)
        {
            rows.TryGetValue(change.Value.Id, out var row);
            if ((row?.Version ?? Guid.Empty) != change.Version)
                throw new DataConflictException();
            if (row?.IsDefault == true || change.Value.IsDefault)
                throw new InvalidDataException();
            var updated = CategoryRules.Save(rows.Values.Select(value => value.ToContract()).ToList(), change.Value);
            var value = updated.Single(category => category.Id == change.Value.Id);
            if (row is null)
            {
                row = value.ToRow(userId);
                database.Categories.Add(row);
                rows.Add(row.Id, row);
            }
            else
            {
                row.CustomName = value.CustomName;
                row.ColorToken = value.ColorToken;
                row.Key = value.Key;
            }
            row.Version = Guid.NewGuid();
            versions[row.Id] = row.Version;
        }
        foreach (var change in request.Deletes)
        {
            if (!rows.TryGetValue(change.Id, out var row) || row.Version != change.Version)
                throw new DataConflictException();
            if (row.IsDefault)
                throw new InvalidDataException();
            var items = await database.Thoughts.Where(item => item.UserId == userId && item.CategoryId == row.Id).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                item.CategoryId = null;
                item.Version = Guid.NewGuid();
                item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            var draft = await database.Drafts.SingleAsync(value => value.UserId == userId, cancellationToken);
            var contract = draft.ToContract();
            if (contract.Review?.Any(item => item.CategoryId == row.Id) == true)
            {
                foreach (var item in contract.Review.Where(item => item.CategoryId == row.Id))
                    item.CategoryId = null;
                draft.SetDraft(contract);
                draft.Version = Guid.NewGuid();
            }
            database.Categories.Remove(row);
            rows.Remove(row.Id);
        }
        StoredDataValidator.Validate(new(), new(), rows.Values.Select(row => row.ToContract()).ToList(), new());
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(null, request.Epoch, versions);
    }
}
