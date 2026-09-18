using Microsoft.AspNetCore.Components;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class StepIndicator
{
    [Parameter]
    public int Active
    {
        get; set;
    }
}
