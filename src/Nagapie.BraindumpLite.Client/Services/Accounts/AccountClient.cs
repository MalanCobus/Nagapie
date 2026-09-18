using System.Net.Http.Json;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Services;

public sealed class AccountClient(HttpClient http, AccountContext context)
{
    public async Task RefreshAsync()
    {
        context.Set(await http.GetFromJsonAsync<AccountSession>("api/account/session"));
    }

    public async Task SignInAsync(string email, string password, bool register)
    {
        await RefreshAsync();
        using var response = await http.PostAsJsonAsync(
            register ? "api/account/register" : "api/account/login", new CredentialsRequest(email, password));
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            throw new ApiClientException(error?.Code ?? ErrorCodes.LoginFailed);
        }
        await RefreshAsync();
    }

    public async Task SignOutAsync()
    {
        using var response = await http.PostAsync("api/account/logout", null);
        response.EnsureSuccessStatusCode();
        context.Set(null);
    }
}
