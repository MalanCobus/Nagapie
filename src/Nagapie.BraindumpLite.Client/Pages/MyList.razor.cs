using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Pages;

public partial class MyList
{
    private string filter = "";
    private BrainDumpItem? editing, deleting, calendar, undoItem;
    private IEnumerable<BrainDumpItem> Filtered => State.Store.Items.Where(i => filter == "" || (filter == "unsorted" ? i.CategoryId is null : i.CategoryId?.ToString() == filter));

    private Task ArchiveAsync(BrainDumpItem item, string reason) => Run(async () =>
    {
        await State.ArchiveAsync(item, reason);
        undoItem = item;
    });
    private Task RestoreAsync(BrainDumpItem item) => Run(() => State.RestoreAsync(item));
    private Task UndoAsync() => Run(async () =>
    {
        if (undoItem is not null)
        {
            await State.UpdateAsync(undoItem);
            undoItem = null;
        }
    });
    private Task DeleteAsync() => Run(async () =>
    {
        await State.DeleteAsync(deleting!.Id);
        if (undoItem?.Id == deleting.Id)
        {
            undoItem = null;
        }

        deleting = null;
    });
    private Task SaveEditAsync() => Run(async () =>
    {
        if (State.Categories.Any(c => c.Id == editing!.CategoryId && c.Key == "let-go"))
        {
            editing = editing! with
            {
                CompletionReason = "let-go",
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        await State.UpdateAsync(editing!);
        editing = null;
    });
}
