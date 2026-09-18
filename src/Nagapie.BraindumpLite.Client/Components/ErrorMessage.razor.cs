using Microsoft.AspNetCore.Components;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class ErrorMessage
{
    [Parameter]
    public string? Code
    {
        get; set;
    }
}
