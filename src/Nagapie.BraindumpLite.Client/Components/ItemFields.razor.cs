using Microsoft.AspNetCore.Components;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class ItemFields
{
    [Parameter, EditorRequired]
    public BrainDumpItem Item { get; set; } = default!;

    [Parameter]
    public EventCallback OnChanged
    {
        get; set;
    }

    private string textId = "text-" + Guid.NewGuid();
    private Task Changed() => OnChanged.InvokeAsync();
}
