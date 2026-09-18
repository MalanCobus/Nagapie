using System.Net;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Nagapie.BraindumpLite.Client.Services;

public sealed class AccountHttpHandler(AccountContext account) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.SameOrigin);
        if (account.Session is { } session)
        {
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", session.RequestToken);
            if (session.UserId is not null)
            {
                request.Headers.TryAddWithoutValidation("X-Account-Id", session.UserId);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            account.Set(null);
        }

        return response;
    }
}

