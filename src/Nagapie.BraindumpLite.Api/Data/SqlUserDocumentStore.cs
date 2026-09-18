using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api.Data;

// Small preferences/access documents remain JSON. Business entities use relational storage.
public sealed class SqlUserDocumentStore(NagapieDbContext database, IRelationalDataStore relational) : IUserDocumentStore
{
    public async Task<UserDocumentResponse> ReadAsync(string userId, string key, CancellationToken cancellationToken)
    {
        if (key is "items" or "categories" or "draft")
            return await relational.ReadAsync(userId, key, cancellationToken);
        if (key == "access")
        {
            var entitled = await database.RelationalAccounts.AnyAsync(row => row.UserId == userId && row.LicenseHash != null, cancellationToken);
            return new(JsonSerializer.SerializeToElement(new Nagapie.BraindumpLite.Contracts.Domain.AccessState
            {
                UnlockToken = entitled ? "account-entitlement" : null
            }, JsonSerializerOptions.Web), Guid.Empty);
        }
        var document = await database.UserDocuments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == userId && row.Key == key, cancellationToken);
        return new(document?.Json is { } json ? JsonSerializer.Deserialize<JsonElement>(json) : null, document?.Version ?? Guid.Empty);
    }

    public async Task<Guid?> SaveAsync(string userId, string key, JsonElement? data, Guid version, CancellationToken cancellationToken)
    {
        if (key is "items" or "categories")
            return null; // Old clients must reload; whole-list replacement is no longer supported.
        if (key == "access")
            return Guid.Empty; // Presentation is derived from the server entitlement.
        if (key == "draft")
            return await relational.SaveDraftAsync(userId,
            data is { } value ? new(value, version) : null, version, cancellationToken);
        var document = await database.UserDocuments.SingleOrDefaultAsync(row => row.UserId == userId && row.Key == key, cancellationToken);
        if ((document?.Version ?? Guid.Empty) != version)
            return null;
        if (document is null)
        {
            document = new()
            {
                UserId = userId,
                Key = key
            };
            database.UserDocuments.Add(document);
        }
        document.Json = data?.GetRawText();
        document.Version = Guid.NewGuid();
        document.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 }) { return null; }
        return document.Version;
    }
}
