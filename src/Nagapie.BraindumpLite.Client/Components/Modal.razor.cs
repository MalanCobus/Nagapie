using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class Modal
{
    [Parameter]
    public string Title { get; set; } = "";

    [Parameter]
    public RenderFragment? ChildContent
    {
        get; set;
    }

    [Parameter]
    public EventCallback Closed
    {
        get; set;
    }

    private ElementReference dialog;
    private readonly string titleId = "dialog-" + Guid.NewGuid();
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JS.InvokeVoidAsync("nagapie.openDialog", dialog);
        }
    }

    private Task OnClosed() => Closed.InvokeAsync();
    public async ValueTask DisposeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("nagapie.closeDialog", dialog);
        }
        catch (JSException)
        {
        }
    }
}
