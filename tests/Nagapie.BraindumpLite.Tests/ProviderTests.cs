using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Contracts;
namespace Nagapie.BraindumpLite.Tests;
public class ProviderTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["AiProvider:ApiKey"] = "test-only-key", ["AiProvider:Model"] = "test-model", ["AiProvider:BaseUrl"] = "https://example.invalid/v1/"
    }).Build();
    private static ProcessDumpRequest Request() => new(Guid.NewGuid(), "An example thought", "en-US", "text", [], null);
    [Fact] public async Task ProviderUsesStructuredOutputAndKeepsUserContentInDataMessage()
    {
        using var http = new HttpClient(new Handler(async message => {
            Assert.Equal("Bearer", message.Headers.Authorization!.Scheme);
            Assert.Equal("test-only-key", message.Headers.Authorization.Parameter);
            Assert.Equal("https://example.invalid/v1/chat/completions", message.RequestUri!.AbsoluteUri);
            using var payload = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
            Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal("json_schema", payload.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            Assert.DoesNotContain("An example thought", payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            Assert.Contains("An example thought", payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            var content = JsonSerializer.Serialize(new { items = new[] { new { text = "An example thought", suggestedCategoryId = (string?)null, planningHorizon = "later" } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content } } } }), Encoding.UTF8, "application/json") };
        }));
        var input = Request(); var result = await new AiProcessor(http, Configuration()).ProcessAsync(input, CancellationToken.None);
        Assert.Equal(input.SourceDumpId, result.SourceDumpId); Assert.Single(result.Items);
    }
    [Fact] public async Task ProviderErrorDoesNotExposeResponseBody()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("private provider diagnostic with a secret") })));
        var ex = await Assert.ThrowsAsync<AiFailure>(() => new AiProcessor(http, Configuration()).ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal("AI_REQUEST_INVALID", ex.Code); Assert.DoesNotContain("private", ex.Message);
    }
    [Theory]
    [InlineData(401, "invalid_api_key", "AI_AUTHENTICATION")]
    [InlineData(403, null, "AI_ACCESS_DENIED")]
    [InlineData(429, "insufficient_quota", "AI_QUOTA")]
    [InlineData(429, "billing_hard_limit_reached", "AI_QUOTA")]
    [InlineData(429, "rate_limit_exceeded", "AI_RATE_LIMITED")]
    [InlineData(404, "model_not_found", "AI_MODEL")]
    [InlineData(400, "invalid_json_schema", "AI_REQUEST_INVALID")]
    [InlineData(500, "private-secret", "AI_UNAVAILABLE")]
    public async Task ProviderFailuresUseSafeActionableCodes(int status, string? code, string expected)
    {
        var body = JsonSerializer.Serialize(new { error = new { code, message = "private-secret" } });
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        var ex = await Assert.ThrowsAsync<AiFailure>(() => new AiProcessor(http, Configuration()).ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal(expected, ex.Code);
        Assert.DoesNotContain("private-secret", ex.Message);
    }
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"error\":null}")]
    [InlineData("{\"error\":{\"code\":[]}}")]
    public async Task UnexpectedErrorBodiesPreserveStatusClassification(string body)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(body) })));
        var ex = await Assert.ThrowsAsync<AiFailure>(() => new AiProcessor(http, Configuration()).ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal("AI_AUTHENTICATION", ex.Code);
    }
    [Fact] public async Task ProviderTimeoutBecomesOwnErrorCode()
    {
        using var http = new HttpClient(new Handler(_ => throw new TaskCanceledException()));
        var ex = await Assert.ThrowsAsync<AiFailure>(() => new AiProcessor(http, Configuration()).ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal("AI_TIMEOUT", ex.Code);
    }
    [Fact] public async Task TruncatedOutputIsRejected()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"finish_reason":"length","message":{"content":"{}"}}]}""") })));
        var ex = await Assert.ThrowsAsync<AiFailure>(() => new AiProcessor(http, Configuration()).ProcessAsync(Request(), CancellationToken.None));
        Assert.Equal("AI_INVALID_OUTPUT", ex.Code);
    }
}
