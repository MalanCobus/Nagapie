using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Pages;

public partial class Dumps
{
    private List<SavedDumpResponse>? dumps;
    private int page;
    private bool hasMore;
    protected override Task OnInitializedAsync() => LoadMoreAsync();
    private Task LoadMoreAsync() => Run(async () =>
    {
        var next = await Api.GetSavedDumpsAsync(page: page);
        dumps ??= [];
        dumps.AddRange(next);
        hasMore = next.Count == 50;
        page++;
    });
}
