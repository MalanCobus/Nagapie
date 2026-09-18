namespace Nagapie.BraindumpLite.Contracts.Domain;

public static class ThoughtRules
{
    public static ItemStore Commit(ItemStore store, Draft draft, IReadOnlyCollection<Category> categories, DateTimeOffset now)
    {
        var updated = store with
        {
            Items = [.. store.Items],
            SavedDumps = [.. store.SavedDumps],
            AiDumps = [.. store.AiDumps]
        };
        foreach (var item in draft.Review!)
        {
            var letGo = categories.Any(category => category.Id == item.CategoryId && category.Key == "let-go");
            updated.Items.Add(item with
            {
                Text = item.Text.Trim(),
                SourceDumpId = draft.Id,
                CompletionReason = letGo ? "let-go" : null,
                CompletedAtUtc = letGo ? now : null
            });
        }

        updated.SavedDumps.Add(draft.Id);
        if (draft.WasAiProcessed)
        {
            updated.AiDumps.Add(draft.Id);
        }

        return updated;
    }

    public static ItemStore Update(ItemStore store, BrainDumpItem item, DateTimeOffset now) => store with
    {
        Items = store.Items.Select(existing => existing.Id == item.Id ? item with { Text = item.Text.Trim(), UpdatedAtUtc = now } : existing).ToList()
    };
}
