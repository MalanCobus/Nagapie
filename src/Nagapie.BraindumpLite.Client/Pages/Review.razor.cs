using Microsoft.AspNetCore.Components;
using Nagapie.BraindumpLite.Client.Domain;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Pages;

public partial class Review
{
    [Parameter]
    public Guid SessionId
    {
        get; set;
    }

    private BrainDumpItem? deleting;
    private Task SaveDraftAsync() => Run(State.SaveDraftAsync);
    private Task SaveAsync() => Run(async () =>
    {
        await State.CommitAsync();
        Nav.NavigateTo("/list");
    });
    private Task RemoveAsync() => Run(async () =>
    {
        var next = State.Draft.Review!.Where(i => i.Id != deleting!.Id).ToList();
        await State.SetReviewAsync(next, State.Draft.WasAiProcessed);
        deleting = null;
    });
    private Task MergeAsync(BrainDumpItem item) => Run(async () =>
    {
        var items = State.Draft.Review!.Select(i => i with { }).ToList();
        var index = items.FindIndex(i => i.Id == item.Id);
        if (index < 0 || index + 1 >= items.Count)
        {
            return;
        }

        var merged = items[index].Text.Trim() + " " + items[index + 1].Text.Trim();
        if (merged.Length > 5000)
        {
            Error = ErrorCodes.Merge;
            return;
        }

        items[index].Text = merged;
        items.RemoveAt(index + 1);
        await State.SetReviewAsync(items, State.Draft.WasAiProcessed);
    });
}
