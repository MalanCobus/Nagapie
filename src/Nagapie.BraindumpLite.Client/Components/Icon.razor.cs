using Microsoft.AspNetCore.Components;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class Icon
{
    [Parameter]
    public string Name { get; set; } = "leaf";

    [Parameter]
    public int Size { get; set; } = 20;
}
