using Microsoft.AspNetCore.Components;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Components;

public partial class AccountForm
{
    [Parameter]
    public bool Register
    {
        get; set;
    }
    private string email = "";
    private string password = "";
    private string confirmation = "";
    private string? error;
    private bool busy;

    private async Task SubmitAsync()
    {
        if (busy)
        {
            return;
        }

        if (Register && password != confirmation)
        {
            error = ErrorCodes.PasswordMismatch;
            return;
        }
        busy = true;
        error = null;
        try
        {
            await Accounts.SignInAsync(email, password, Register);
            password = confirmation = "";
            Navigation.NavigateTo("/", forceLoad: true);
        }
        catch (ApiClientException exception) { error = exception.Code; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            error = ErrorCodes.AccountUnavailable;
        }
        finally { busy = false; }
    }
}
