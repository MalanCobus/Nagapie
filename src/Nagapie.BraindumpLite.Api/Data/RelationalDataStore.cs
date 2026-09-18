using System.Data;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed partial class RelationalDataStore(NagapieDbContext database, LegacyDataImporter importer,
    Microsoft.Extensions.Options.IOptions<AccessOptions> access) : IRelationalDataStore
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
        if (key == "items")
        {
            var page = await QueryThoughtsAsync(userId, new(), cancellationToken);
            return new(Json(new ItemStore { Items = page.Items }), page.Epoch, page.RowVersions);
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var epoch = await database.RelationalAccounts.Where(row => row.UserId == userId).Select(row => row.Epoch).SingleAsync(cancellationToken);
            var categories = await database.Categories.AsNoTracking().Where(row => row.UserId == userId).ToListAsync(cancellationToken);
            if (epoch == await database.RelationalAccounts.Where(row => row.UserId == userId).Select(row => row.Epoch).SingleAsync(cancellationToken))
                return new(Json(categories.OrderBy(row => row.Id).Select(row => row.ToContract())), epoch,
                    categories.ToDictionary(row => row.Id, row => row.Version));
        }
        throw new DataConflictException();
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
        // A draft ID is a stable operation ID for old clients too. Include the full payload
        // so reusing an operation ID for a different mutation is always a conflict.
        var operationId = request.OperationId != Guid.Empty ? request.OperationId : request.CommitDumpId;
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, JsonSerializerOptions.Web)));
        if (operationId is { } retryId)
        {
            var receipt = await database.OperationReceipts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.UserId == userId && row.Id == retryId, cancellationToken);
            if (receipt is not null)
            {
                if (receipt.RequestHash != requestHash)
                    throw new DataConflictException();
                return JsonSerializer.Deserialize<UserDocumentResponse>(receipt.ResponseJson, JsonSerializerOptions.Web)!;
            }
        }
        var rows = await database.Thoughts.Where(row => row.UserId == userId && ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        if (request.CommitDumpId is { } commitId)
        {
            if (await database.BrainDumps.AnyAsync(row => row.UserId == userId && row.Id == commitId && row.IsCommitted, cancellationToken))
                throw new DataConflictException();
            var draft = await database.Drafts.SingleAsync(row => row.UserId == userId, cancellationToken);
            if (draft.IsDeleted || draft.Id != commitId || draft.Version != request.DraftVersion)
                throw new DataConflictException();
            if (request.Upserts.Count is < 1 or > 30 || request.Deletes.Count != 0 || request.Upserts.Any(change => change.Version != Guid.Empty || change.Value.SourceDumpId != commitId))
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
            dump.WasAiProcessed = await database.ProcessedDumps.AnyAsync(row => row.UserId == userId && row.Id == commitId, cancellationToken);
        }
        var categories = await database.Categories.Where(row => row.UserId == userId).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var changedVersions = new Dictionary<Guid, Guid>();
        foreach (var change in request.Upserts)
        {
            rows.TryGetValue(change.Value.Id, out var row);
            var restoringLetGo = row?.CompletionReason == "let-go" && change.Value.CompletionReason is null;
            if ((row?.Version ?? Guid.Empty) != change.Version)
                throw new DataConflictException();
            if (change.Value.CategoryId is { } categoryId && !categories.Any(category => category.Id == categoryId))
                throw new InvalidDataException();
            if (row is null)
            {
                if (request.CommitDumpId is null || change.Value.SourceDumpId != request.CommitDumpId)
                    throw new InvalidDataException();
                row = change.Value.ToRow(userId);
                row.Text = row.Text.Trim();
                row.CreatedAtUtc = now;
                row.UpdatedAtUtc = now;
                database.Thoughts.Add(row);
            }
            else
            {
                if ((row.SourceDumpId ?? Guid.Empty) != change.Value.SourceDumpId)
                    throw new InvalidDataException();
                row.Text = change.Value.Text.Trim();
                row.CategoryId = change.Value.CategoryId;
                row.PlanningHorizon = change.Value.PlanningHorizon;
                row.PlannedDate = change.Value.PlannedDate;
                row.CompletionReason = change.Value.CompletionReason;
                row.UpdatedAtUtc = now;
            }
            var letGo = categories.Any(category => category.Id == row.CategoryId && category.Key == "let-go");
            if (restoringLetGo && letGo)
            {
                row.CategoryId = null;
                letGo = false;
            }
            row.CompletionReason = letGo ? "let-go" : change.Value.CompletionReason;
            row.CompletedAtUtc = row.CompletionReason is null ? null : row.CompletedAtUtc ?? now;
            // Completion time belongs to the transition, never to the caller.
            if (row.CompletionReason is not null && (change.Version == Guid.Empty ||
                database.Entry(row).Property(value => value.CompletionReason).OriginalValue != row.CompletionReason))
                row.CompletedAtUtc = now;
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
        var result = new UserDocumentResponse(Json(request.Upserts.Select(change =>
            database.ChangeTracker.Entries<Thought>().Single(entry => entry.Entity.Id == change.Value.Id).Entity.ToContract()).ToList()),
            request.Epoch, changedVersions);
        if (operationId is { } receiptId)
            database.OperationReceipts.Add(new()
            {
                UserId = userId,
                Id = receiptId,
                RequestHash = requestHash,
                ResponseJson = JsonSerializer.Serialize(result, JsonSerializerOptions.Web)
            });
        if (request.CommitDumpId is { } committedId)
        {
            var dump = database.BrainDumps.Local.Single(row => row.UserId == userId && row.Id == committedId);
            if (dump.WasAiProcessed)
            {
                var account = await database.RelationalAccounts.SingleAsync(row => row.UserId == userId, cancellationToken);
                if (access.Value.PaywallEnabled && account.LicenseHash is null && account.SuccessfulAiDumps >= AccessOptions.FreeDumpLimit)
                    throw new TrialLimitException();
                account.SuccessfulAiDumps++;
            }
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
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
        if (request is null && row.IsDeleted)
            return row.Version;
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
