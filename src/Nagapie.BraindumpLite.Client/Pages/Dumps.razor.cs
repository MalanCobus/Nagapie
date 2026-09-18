using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Pages;

public partial class Dumps
{
    private List<SavedDumpResponse>? dumps;
    protected override Task OnInitializedAsync() => Run(async () =>
    {
        dumps = await Api.GetSavedDumpsAsync();
    });
}
