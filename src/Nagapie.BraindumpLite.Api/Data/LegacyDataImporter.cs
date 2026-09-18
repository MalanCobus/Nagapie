using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed class LegacyDataImporter(NagapieDbContext database, ILogger<LegacyDataImporter> logger)
{
    public async Task ImportAllAsync(CancellationToken cancellationToken)
    {
        var users = await database.Users.Where(user => !database.RelationalAccounts.Any(row => row.UserId == user.Id))
            .Select(user => user.Id).ToListAsync(cancellationToken);
        var failures = 0;
        foreach (var userId in users)
        {
            try
            {
                await EnsureImportedAsync(userId, cancellationToken);
            }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                failures++;
                logger.LogError("Legacy import validation failed for account {AccountId}; original documents retained. Failure type {FailureType}", userId, error.GetType().Name);
            }
            finally { database.ChangeTracker.Clear(); }
        }
        failures += await UpgradeDraftPlanningAsync(cancellationToken);
        if (failures > 0)
            throw new InvalidDataException($"Legacy import validation failed for {failures} account(s). See account IDs in deployment logs.");
    }

    public async Task EnsureImportedAsync(string userId, CancellationToken cancellationToken)
    {
        if (await database.RelationalAccounts.AnyAsync(row => row.UserId == userId, cancellationToken))
            return;
        await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, cancellationToken);
        if (await database.RelationalAccounts.AnyAsync(row => row.UserId == userId, cancellationToken))
            return;
        var documents = await database.UserDocuments.AsNoTracking().Where(row => row.UserId == userId)
            .ToDictionaryAsync(row => row.Key, cancellationToken);
        T? Read<T>(string key) => documents.GetValueOrDefault(key)?.Json is { } json
            ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) : default;
        var categories = Read<List<Category>>("categories") ?? CategoryRules.CreateDefaults();
        var items = Read<ItemStore>("items") ?? new();
        var draft = Read<Draft>("draft");
        if (items.Items is null)
            throw new InvalidDataException();
        PlanningHorizons.UpgradeLegacy(items.Items);
        PlanningHorizons.UpgradeLegacy(draft?.Review ?? []);
        StoredDataValidator.Validate(new(), items, categories, draft ?? new());
        if (items.SavedDumps.Contains(Guid.Empty) || items.AiDumps.Contains(Guid.Empty))
            throw new InvalidDataException("Legacy data contains an invalid dump receipt. Original data retained.");
        var categoryIds = categories.Select(row => row.Id).ToHashSet();
        if (items.Items.Any(item => item.CategoryId is { } id && !categoryIds.Contains(id)))
            throw new InvalidDataException("Legacy data contains an unresolved category. Import stopped; original data retained.");
        database.Categories.AddRange(categories.Select(category => category.ToRow(userId)));
        var dumps = await database.BrainDumps.Where(row => row.UserId == userId).ToDictionaryAsync(row => row.Id, cancellationToken);
        var dumpIds = items.SavedDumps.Union(items.AiDumps).Union(items.Items.Select(row => row.SourceDumpId))
            .Where(id => id != Guid.Empty).ToHashSet();
        foreach (var id in dumpIds)
        {
            if (!dumps.TryGetValue(id, out var dump))
            {
                // Early versions did not retain every original. Preserve IDs without inventing original text.
                dump = new SavedBrainDump { UserId = userId, Id = id, OriginalAvailable = false };
                database.BrainDumps.Add(dump);
                dumps.Add(id, dump);
            }
            dump.IsCommitted = items.SavedDumps.Contains(id);
            dump.WasAiProcessed = items.AiDumps.Contains(id);
        }
        database.Thoughts.AddRange(items.Items.Select(item => item.ToRow(userId)));
        var draftRow = new UserDraft
        {
            UserId = userId,
            IsDeleted = draft is null,
            Version = documents.GetValueOrDefault("draft")?.Version ?? Guid.NewGuid()
        };
        if (draft is not null)
            draftRow.SetDraft(draft);
        database.Drafts.Add(draftRow);
        // Keep original JSON as a recovery archive. The marker prevents any later re-import after a reset.
        database.RelationalAccounts.Add(new()
        {
            UserId = userId,
            SuccessfulAiDumps = items.AiDumps.Count
        });
        await database.SaveChangesAsync(cancellationToken);
        if (await database.Thoughts.CountAsync(row => row.UserId == userId, cancellationToken) != items.Items.Count ||
            await database.Categories.CountAsync(row => row.UserId == userId, cancellationToken) != categories.Count)
            throw new InvalidDataException("Relational import verification failed. Original data retained.");
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<int> UpgradeDraftPlanningAsync(CancellationToken cancellationToken)
    {
        var users = await database.Drafts.Where(row => row.ReviewJson != null && row.ReviewJson.Contains("next-week"))
            .Select(row => row.UserId).ToListAsync(cancellationToken);
        var failures = 0;
        foreach (var userId in users)
        {
            try
            {
                await using var transaction = await RelationalTransactions.BeginWriteAsync(database, userId, cancellationToken);
                var row = await database.Drafts.SingleAsync(row => row.UserId == userId, cancellationToken);
                var draft = row.ToContract();
                if (PlanningHorizons.UpgradeLegacy(draft.Review ?? []))
                {
                    StoredDataValidator.Validate(new(), new(), [], draft);
                    row.SetDraft(draft);
                    row.Version = Guid.NewGuid();
                    await database.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                failures++;
                logger.LogError("Draft upgrade validation failed for account {AccountId}; original retained. Failure type {FailureType}", userId, error.GetType().Name);
            }
            finally { database.ChangeTracker.Clear(); }
        }
        return failures;
    }
}
