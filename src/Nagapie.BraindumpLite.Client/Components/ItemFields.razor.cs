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
    private Task HorizonChanged()
    {
        Item.PlannedDate = Item.PlanningHorizon == "date" ? Item.PlannedDate ?? DateOnly.FromDateTime(DateTime.Today) : null;
        return Changed();
    }
    private Task CategoryCreated(Guid id)
    {
        Item.CategoryId = id;
        return Changed();
    }
}
