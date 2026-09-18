using System.Net;
using System.Net.Http.Json;
using Nagapie.BraindumpLite.Client.Domain;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Tests;

public class ApiClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private static ProcessDumpRequest Request() =>
        new(Guid.NewGuid(), "Buy bread", "en-US", "text", [], null);

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, ErrorCodes.PaywallRequired)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorCodes.AiAuthentication)]
    public async Task FailedProcessingPreservesActionableError(HttpStatusCode status, string code)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(new ApiError(code, "test"))
        })))
        {
            BaseAddress = new Uri("http://localhost")
        };

        var exception = await Assert.ThrowsAsync<ApiClientException>(() =>
            new NagapieApiClient(http).ProcessDumpAsync(Request(), CancellationToken.None));

        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task ProcessingRejectsResponseForAnotherDraft()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ProcessDumpResponse(Guid.NewGuid(), [new("Buy bread", null, "later")], "1"))
        })))
        {
            BaseAddress = new Uri("http://localhost")
        };

        var exception = await Assert.ThrowsAsync<ApiClientException>(() =>
            new NagapieApiClient(http).ProcessDumpAsync(Request(), CancellationToken.None));

        Assert.Equal(ErrorCodes.AiInvalidOutput, exception.Code);
    }

    [Fact]
    public async Task ProcessingUsesExpectedRouteAndAcceptsMatchingDraft()
    {
        var request = Request();
        using var http = new HttpClient(new Handler(async (message, cancellationToken) =>
        {
            Assert.Equal("/api/dumps/process", message.RequestUri!.AbsolutePath);
            var sent = await message.Content!.ReadFromJsonAsync<ProcessDumpRequest>(cancellationToken);
            Assert.Equal(request.SourceDumpId, sent!.SourceDumpId);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProcessDumpResponse(request.SourceDumpId, [new("Buy bread", null, "later")], "1"))
            };
        }))
        {
            BaseAddress = new Uri("http://localhost")
        };

        var result = await new NagapieApiClient(http).ProcessDumpAsync(request, CancellationToken.None);
        Assert.Equal("Buy bread", Assert.Single(result.Items).Text);
    }

    [Fact]
    public async Task CancellationReachesTransport()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation should have stopped the request.");
        }))
        {
            BaseAddress = new Uri("http://localhost")
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new NagapieApiClient(http).ProcessDumpAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task MalformedLicenseResponseHasSafeError()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json")
        })))
        {
            BaseAddress = new Uri("http://localhost")
        };

        var exception = await Assert.ThrowsAsync<ApiClientException>(() =>
            new NagapieApiClient(http).VerifyLicenseAsync("test"));

        Assert.Equal(ErrorCodes.LicenseInvalid, exception.Code);
    }

    [Fact]
    public void ReviewMappingDiscardsUnknownCategoriesAndHorizons()
    {
        var response = new ProcessDumpResponse(Guid.NewGuid(),
            [new("Buy bread", Guid.NewGuid(), "unexpected")], "1");

        var item = Assert.Single(DumpReviewMapper.Map(response, CategoryRules.CreateDefaults(), "speech"));

        Assert.Null(item.CategoryId);
        Assert.Equal("later", item.PlanningHorizon);
        Assert.Equal("speech", item.InputMethod);
        Assert.Equal(response.SourceDumpId, item.SourceDumpId);
    }
}
