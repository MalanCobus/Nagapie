using Microsoft.AspNetCore.Components;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class CategoryCreator
{
    [Parameter]
    public EventCallback<Guid> Created
    {
        get; set;
    }
    private string name = "";

    private Task AddAsync() => Run(async () =>
    {
        var id = Guid.NewGuid();
        await State.SaveCategoryAsync(new(id, "custom", name, "sage", false));
        name = "";
        await Created.InvokeAsync(id);
    });
}
