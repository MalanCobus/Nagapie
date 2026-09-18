using Microsoft.JSInterop;

namespace Nagapie.BraindumpLite.Client.Layout;

public partial class MainLayout
{
    private bool reset;
    private string? renderedLocation;
    protected override async Task OnInitializedAsync()
    {
        State.Changed += Update;
        await State.InitializeAsync();
    }

    private void Update() => InvokeAsync(StateHasChanged);
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (renderedLocation != Nav.Uri)
        {
            renderedLocation = Nav.Uri;
            await JS.InvokeVoidAsync("nagapie.pageTop");
        }
    }

    private async Task ResetAsync()
    {
        try
        {
            await State.ClearAsync();
            reset = false;
            Nav.NavigateTo("/welcome");
        }
        catch (JSException)
        {
        }
    }

    public void Dispose() => State.Changed -= Update;
}
