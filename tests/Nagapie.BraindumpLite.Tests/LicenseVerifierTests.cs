using System.Net;
using Microsoft.Extensions.Options;
using Nagapie.BraindumpLite.Api;

namespace Nagapie.BraindumpLite.Tests;

public class LicenseVerifierTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }

    private sealed class Tokens : IUnlockTokenService
    {
        public string? IssuedFor
        {
            get; private set;
        }
        public string Issue(string license)
        {
            IssuedFor = license;
            return "signed-test-token";
        }
        public bool Verify(string? token) => token == "signed-test-token";
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyEnabledLicensesReceiveToken(bool enabled)
    {
        var tokens = new Tokens();
        using var http = new HttpClient(new Handler(message =>
        {
            Assert.Equal("https://payhip.com/api/v2/license/verify?license_key=test%26license", message.RequestUri!.AbsoluteUri);
            Assert.Equal("test-secret", Assert.Single(message.Headers.GetValues("product-secret-key")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(enabled ? "{\"data\":{\"enabled\":true}}" : "{\"data\":{\"enabled\":false}}")
            });
        }));

        var verifier = Create(http, tokens);
        var result = await verifier.VerifyAsync(" test&license ", CancellationToken.None);

        Assert.Equal(enabled, result.IsValid);
        Assert.Equal(enabled ? "signed-test-token" : null, result.UnlockToken);
        Assert.Equal(enabled ? "test&license" : null, tokens.IssuedFor);
    }

    [Fact]
    public async Task MissingConfigurationDoesNotContactProvider()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Must not send.")));
        var verifier = new PayhipLicenseVerifier(http, Options.Create(new PayhipOptions()),
            Options.Create(new UnlockTokenOptions()), new Tokens());

        await Assert.ThrowsAsync<LicenseUnavailableException>(() => verifier.VerifyAsync("test", CancellationToken.None));
    }

    [Fact]
    public async Task ProviderFailureDoesNotExposeDiagnostics()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("private provider diagnostic")
        })));
        var tokens = new Tokens();

        var error = await Assert.ThrowsAsync<LicenseUnavailableException>(() =>
            Create(http, tokens).VerifyAsync("test", CancellationToken.None));

        Assert.DoesNotContain("private", error.Message);
        Assert.Null(tokens.IssuedFor);
    }

    [Fact]
    public async Task CallerCancellationIsNotChangedIntoProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(http, new Tokens()).VerifyAsync("test", cancellation.Token));
    }

    private static PayhipLicenseVerifier Create(HttpClient http, IUnlockTokenService tokens) =>
        new(http, Options.Create(new PayhipOptions { ProductSecret = "test-secret" }),
            Options.Create(new UnlockTokenOptions { SigningKey = new string('a', 48) }), tokens);
}
