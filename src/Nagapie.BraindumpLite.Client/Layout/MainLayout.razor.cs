using Nagapie.BraindumpLite.Contracts;
namespace Nagapie.BraindumpLite.Client.Layout;

public partial class MainLayout
{
    private bool accountReady;
    private string? accountError;
    private string? accountId;

    protected override async Task OnInitializedAsync()
    {
        State.Changed += Update;
        Account.Changed += AccountChanged;
        try
        {
            await Accounts.RefreshAsync();
            if (Account.IsAuthenticated)
            {
                await State.InitializeAsync();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            accountError = ErrorCodes.AccountUnavailable;
        }
        finally { accountReady = true; }
    }

    private void AccountChanged()
    {
        if (accountId != Account.Session?.UserId)
        {
            accountId = Account.Session?.UserId;
            State.ResetMemory();
        }
        Update();
    }

    private void Update() => InvokeAsync(StateHasChanged);
    private void Reload() => Nav.NavigateTo(Nav.Uri, forceLoad: true);

    private async Task LogoutAsync()
    {
        try
        {
            await Accounts.SignOutAsync();
            Nav.NavigateTo("/login", forceLoad: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            accountError = ErrorCodes.AccountUnavailable;
        }
    }

    public void Dispose()
    {
        State.Changed -= Update;
        Account.Changed -= AccountChanged;
    }
}
