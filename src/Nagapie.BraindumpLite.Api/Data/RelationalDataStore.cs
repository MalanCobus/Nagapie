using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed partial class RelationalDataStore(NagapieDbContext database, LegacyDataImporter importer) : IRelationalDataStore
{
    private static JsonElement Json<T>(T data) => JsonSerializer.SerializeToElement(data, JsonSerializerOptions.Web);

    public async Task<UserDocumentResponse> ReadAsync(string userId, string key, CancellationToken cancellationToken)
    {
        await importer.EnsureImportedAsync(userId, cancellationToken);
        if (key == "draft")
        {
            var draft = await database.Drafts.AsNoTracking().SingleAsync(row => row.UserId == userId, cancellationToken);
            return new(draft.IsDeleted ? null : Json(draft.ToContract()), draft.Version);
        }
        // Keep the data and its reset token in the same consistent snapshot.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var epoch = await database.RelationalAccounts.Where(row => row.UserId == userId).Select(row => row.Epoch).SingleAsync(cancellationToken);
        UserDocumentResponse response;
        if (key == "categories")
        {
            var categories = await database.Categories.AsNoTracking().Where(row => row.UserId == userId).ToListAsync(cancellationToken);
            response = new(Json(categories.OrderBy(row => row.Id).Select(row => row.ToContract())), epoch,
                categories.ToDictionary(row => row.Id, row => row.Version));
        }
        else
        {
            var items = await database.Thoughts.AsNoTracking().Where(row => row.UserId == userId).ToListAsync(cancellationToken);
            var dumps = await database.BrainDumps.AsNoTracking().Where(row => row.UserId == userId && (row.IsCommitted || row.WasAiProcessed))
                .Select(row => new { row.Id, row.IsCommitted, row.WasAiProcessed }).ToListAsync(cancellationToken);
            response = new(Json(new ItemStore
            {
                Items = items.Select(row => row.ToContract()).ToList(),
                SavedDumps = dumps.Where(row => row.IsCommitted).Select(row => row.Id).ToHashSet(),
                AiDumps = dumps.Where(row => row.WasAiProcessed).Select(row => row.Id).ToHashSet()
            }), epoch, items.ToDictionary(row => row.Id, row => row.Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<UserDocumentResponse> SaveThoughtsAsync(string userId, SaveThoughtsRequest request, CancellationToken cancellationToken)
    {
        if (request.Upserts is null || request.Deletes is null || request.Upserts.Count + request.Deletes.Count > 5000 ||
            request.Upserts.Any(change => change?.Value is null) || request.Deletes.Any(change => change is null))
            throw new InvalidDataException();
        var ids = request.Upserts.Select(change => change.Value.Id).Concat(request.Deletes.Select(change => change.Id)).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidDataException();
        StoredDataValidator.Validate(new(), new()
        {
            Items = request.Upserts.Select(change => change.Value).ToList()
        }, [], new());
        await importer.EnsureImportedAsync(userId, cancellationToken);
        await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, cancellationToken);
        await RequireEpochAsync(userId, request.Epoch, cancellationToken);
        var rows = await database.Thoughts.Where(row => row.UserId == userId && ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        if (request.CommitDumpId is { } commitId)
        {
            if (await database.BrainDumps.AnyAsync(row => row.UserId == userId && row.Id == commitId && row.IsCommitted, cancellationToken))
                throw new DataConflictException();
            var draft = await database.Drafts.SingleAsync(row => row.UserId == userId, cancellationToken);
            if (draft.IsDeleted || draft.Id != commitId || draft.Version != request.DraftVersion)
                throw new DataConflictException();
            if (request.Upserts.Count == 0 || request.Upserts.Any(change => change.Version != Guid.Empty || change.Value.SourceDumpId != commitId))
                throw new InvalidDataException();
            var dump = await database.BrainDumps.SingleOrDefaultAsync(row => row.UserId == userId && row.Id == commitId, cancellationToken);
            if (dump is null)
            {
                dump = new()
                {
                    UserId = userId,
                    Id = commitId
                };
                database.BrainDumps.Add(dump);
            }
            dump.Text = draft.Text;
            dump.InputMethod = draft.InputMethod;
            dump.OriginalAvailable = true;
            dump.IsCommitted = true;
            dump.WasAiProcessed = draft.WasAiProcessed;
        }
        var categories = await database.Categories.Where(row => row.UserId == userId).Select(row => row.Id).ToListAsync(cancellationToken);
        var changedVersions = new Dictionary<Guid, Guid>();
        foreach (var change in request.Upserts)
        {
            rows.TryGetValue(change.Value.Id, out var row);
            if ((row?.Version ?? Guid.Empty) != change.Version)
                throw new DataConflictException();
            if (change.Value.CategoryId is { } categoryId && !categories.Contains(categoryId))
                throw new InvalidDataException();
            if (row is null)
            {
                if (request.CommitDumpId is null || change.Value.SourceDumpId != request.CommitDumpId)
                    throw new InvalidDataException();
                row = change.Value.ToRow(userId);
                database.Thoughts.Add(row);
            }
            else
            {
                if ((row.SourceDumpId ?? Guid.Empty) != change.Value.SourceDumpId)
                    throw new InvalidDataException();
                row.Text = change.Value.Text.Trim();
                row.CategoryId = change.Value.CategoryId;
                row.PlanningHorizon = change.Value.PlanningHorizon;
                row.CompletionReason = change.Value.CompletionReason;
                row.CompletedAtUtc = change.Value.CompletedAtUtc;
                row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            row.Version = Guid.NewGuid();
            changedVersions[row.Id] = row.Version;
        }
        foreach (var change in request.Deletes)
        {
            if (!rows.TryGetValue(change.Id, out var row) || row.Version != change.Version)
                throw new DataConflictException();
            database.Thoughts.Remove(row);
        }
        var count = await database.Thoughts.CountAsync(row => row.UserId == userId, cancellationToken);
        if (count + request.Upserts.Count(change => !rows.ContainsKey(change.Value.Id)) - request.Deletes.Count > 5000)
            throw new InvalidDataException();
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(null, request.Epoch, changedVersions);
    }

    private async Task RequireEpochAsync(string userId, Guid epoch, CancellationToken cancellationToken)
    {
        if (!await database.RelationalAccounts.AnyAsync(row => row.UserId == userId && row.Epoch == epoch, cancellationToken))
            throw new DataConflictException();
    }

    public async Task<Guid?> SaveDraftAsync(string userId, SaveUserDocumentRequest? request, Guid version, CancellationToken cancellationToken)
    {
        await importer.EnsureImportedAsync(userId, cancellationToken);
        await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, cancellationToken);
        var row = await database.Drafts.SingleAsync(row => row.UserId == userId, cancellationToken);
        if (row.Version != version)
            return null;
        if (request is null)
        {
            row.IsDeleted = true;
            row.Text = "";
            row.ReviewJson = null;
        }
        else
            row.SetDraft(request.Data.Deserialize<Draft>(JsonSerializerOptions.Web)!);
        row.Version = Guid.NewGuid();
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return null; }
        await transaction.CommitAsync(cancellationToken);
        return row.Version;
    }
}
