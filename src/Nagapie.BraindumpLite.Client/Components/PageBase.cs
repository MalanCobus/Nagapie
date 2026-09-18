using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Client.Resources;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Components;

public class PageBase : ComponentBase
{
    [Inject]
    public AppState State { get; set; } = default!;

    [Inject]
    public IStringLocalizer<AppResources> L { get; set; } = default!;

    [Inject]
    public NavigationManager Nav { get; set; } = default!;

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public INagapieApiClient Api { get; set; } = default!;

    protected string? Error;
    protected bool Busy;
    protected string CategoryName(Category c) => c.IsDefault ? L["Category_" + c.Key] : c.CustomName!;
    protected string CategoryName(Guid? id) => State.Categories.FirstOrDefault(c => c.Id == id) is { } c ? CategoryName(c) : L["Category_None"];
    protected async Task Run(Func<Task> action)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        Error = null;
        try
        {
            await action();
        }
        catch (JSException)
        {
            Error = ErrorCodes.Storage;
        }
        catch (InvalidDataException ex)
        {
            Error = ex.Message;
        }
        catch (ApiClientException ex)
        {
            Error = ex.Code;
        }
        catch (HttpRequestException)
        {
            Error = ErrorCodes.AiUnavailable;
        }
        catch (TaskCanceledException)
        {
            Error = ErrorCodes.AiTimeout;
        }
        finally
        {
            Busy = false;
        }
    }
}
