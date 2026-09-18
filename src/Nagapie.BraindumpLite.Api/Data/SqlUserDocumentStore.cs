using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed class SqlUserDocumentStore(NagapieDbContext database) : IUserDocumentStore
{
    public async Task<UserDocumentResponse> ReadAsync(string userId, string key, CancellationToken cancellationToken)
    {
        var document = await database.UserDocuments.AsNoTracking()
            .SingleOrDefaultAsync(document => document.UserId == userId && document.Key == key, cancellationToken);
        var data = document?.Json is { } json ? JsonSerializer.Deserialize<JsonElement>(json) : (JsonElement?)null;
        return new(data, document?.Version ?? Guid.Empty);
    }

    public async Task<Guid?> SaveAsync(
        string userId, string key, JsonElement? data, Guid version, CancellationToken cancellationToken)
    {
        var document = await database.UserDocuments
            .SingleOrDefaultAsync(document => document.UserId == userId && document.Key == key, cancellationToken);
        if ((document?.Version ?? Guid.Empty) != version)
        {
            return null;
        }

        if (document is null)
        {
            document = new UserDocument { UserId = userId, Key = key };
            database.UserDocuments.Add(document);
        }

        // Preserve original text when its reviewed thoughts are committed.
        // The receipt, item list, and original dump are written in one SQL transaction.
        if (key == "items" && data is { } items)
        {
            var store = items.Deserialize<ItemStore>(JsonSerializerOptions.Web)!;
            var draftJson = await database.UserDocuments
                .Where(record => record.UserId == userId && record.Key == "draft")
                .Select(record => record.Json).SingleOrDefaultAsync(cancellationToken);
            var draft = draftJson is null ? null : JsonSerializer.Deserialize<Draft>(draftJson, JsonSerializerOptions.Web);
            if (draft is not null && store.SavedDumps.Contains(draft.Id) &&
                !await database.BrainDumps.AnyAsync(dump => dump.UserId == userId && dump.Id == draft.Id, cancellationToken))
            {
                database.BrainDumps.Add(new SavedBrainDump
                {
                    UserId = userId,
                    Id = draft.Id,
                    Text = draft.Text,
                    InputMethod = draft.InputMethod
                });
            }
        }

        // Keep a versioned tombstone on delete, so stale clients cannot recreate deleted data.
        document.Json = data?.GetRawText();
        document.Version = Guid.NewGuid();
        document.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return document.Version;
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            return null;
        }
    }
}

