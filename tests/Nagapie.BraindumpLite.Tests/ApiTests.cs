using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Tests;

public class ApiTests
{
    private static ProcessDumpRequest Request() => new(Guid.NewGuid(), "Call the dentist today. Remember my idea.", "en-US", "text", [new(Guid.NewGuid(), "do", "Do", true)], null);
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyInputRejected(string text) => Assert.False(DumpValidation.IsValid(Request() with { Text = text }));
    [Fact]
    public void InputLimitsEnforced()
    {
        Assert.False(DumpValidation.IsValid(Request() with
        {
            Text = new string('a', 5001)
        }));
        Assert.False(DumpValidation.IsValid(Request() with
        {
            Language = "fr-FR"
        }));
        Assert.False(DumpValidation.IsValid(Request() with
        {
            AvailableCategories = Enumerable.Range(0, 26).Select(_ => new CategoryReferenceDto(Guid.NewGuid(), "do", "Do", true)).ToList()
        }));
        Assert.True(DumpValidation.IsValid(Request()));
    }

    [Fact]
    public void AiUnknownCategoryAndHorizonAreSafe()
    {
        var result = AiOutputValidator.Validate("""{"items":[{"text":" Dentist ","suggestedCategoryId":"11111111-1111-1111-1111-111111111111","planningHorizon":"urgent"}]}""", Request());
        Assert.Equal("Dentist", result.Items[0].Text);
        Assert.Null(result.Items[0].SuggestedCategoryId);
        Assert.Equal("later", result.Items[0].PlanningHorizon);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[{\"text\":\"\",\"suggestedCategoryId\":null,\"planningHorizon\":\"today\"}]}")]
    public void InvalidOutputRejected(string json) => Assert.Throws<AiFailure>(() => AiOutputValidator.Validate(json, Request()));
    [Fact]
    public void TooManyItemsRejected()
    {
        var json = JsonSerializer.Serialize(new
        {
            items = Enumerable.Range(0, 31).Select(_ => new { text = "Task", suggestedCategoryId = (string?)null, planningHorizon = "later" })
        });
        Assert.Throws<AiFailure>(() => AiOutputValidator.Validate(json, Request()));
    }

    [Fact]
    public void TokensCannotBeTamperedWith()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["UnlockTokens:SigningKey"] = new string('a', 48) }).Build();
        var tokens = new UnlockTokens(Microsoft.Extensions.Options.Options.Create(config.GetSection("UnlockTokens").Get<UnlockTokenOptions>()!));
        var token = tokens.Issue("test-license");
        Assert.True(tokens.Verify(token));
        Assert.False(tokens.Verify(token + "x"));
        Assert.False(tokens.Verify("garbage"));
        Assert.DoesNotContain("test-license", Encoding.UTF8.GetString(Convert.FromBase64String(token.Split('.')[0])));
        var other = new UnlockTokens(Microsoft.Extensions.Options.Options.Create(new UnlockTokenOptions { SigningKey = new string('b', 48) }));
        Assert.False(other.Verify(token));
    }

    private sealed class FakeAi : IAiBrainDumpProcessor
    {
        public Task<ProcessDumpResponse> ProcessAsync(ProcessDumpRequest request, CancellationToken ct) => Task.FromResult(new ProcessDumpResponse(request.SourceDumpId, [new("Call dentist", request.AvailableCategories[0].Id, "today")], "test"));
    }

    private static WebApplicationFactory<Program> Factory(bool paywall = false) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Access:PaywallEnabled"] = paywall.ToString(), ["AiProvider:ApiKey"] = "", ["UnlockTokens:SigningKey"] = new string('a', 48) }));
        builder.ConfigureServices(services => { AccountTestSupport.UseTestDatabase(services); services.AddSingleton<IAiBrainDumpProcessor, FakeAi>(); });
    });
    [Fact]
    public async Task EndpointValidatesAndReturnsStructuredResult()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var valid = await client.PostAsJsonAsync("/api/dumps/process", Request());
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Single((await valid.Content.ReadFromJsonAsync<ProcessDumpResponse>())!.Items);
        var invalid = await client.PostAsJsonAsync("/api/dumps/process", Request() with
        {
            Text = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task TestModeNeverBlocksAndProductionEnforcesSoftLimit()
    {
        using var test = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(test);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/dumps/process", Request() with
        {
            SuccessfulDumpCount = 10
        })).StatusCode);
        using var production = Factory(true);
        using var paidClient = await AccountTestSupport.CreateUserAsync(production);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await paidClient.PostAsJsonAsync("/api/dumps/process", Request() with
        {
            SuccessfulDumpCount = 3
        })).StatusCode);
        var token = production.Services.GetRequiredService<UnlockTokens>().Issue("example");
        Assert.Equal(HttpStatusCode.OK, (await paidClient.PostAsJsonAsync("/api/dumps/process", Request() with
        {
            SuccessfulDumpCount = 3,
            UnlockToken = token
        })).StatusCode);
    }

    [Fact]
    public async Task SecretsNeverAppearInPublicConfig()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var text = await client.GetStringAsync("/api/config");
        Assert.DoesNotContain("ApiKey", text);
        Assert.DoesNotContain("SigningKey", text);
        Assert.DoesNotContain("ProductSecret", text);
    }

    [Fact]
    public async Task RateLimitReturns429()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        HttpResponseMessage? response = null;
        for (int i = 0; i < 16; i++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync("/api/dumps/process", Request());
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        response.Dispose();
    }

    [Fact]
    public async Task MissingKeyReturnsActionableFailure()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(AccountTestSupport.UseTestDatabase).ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["AiProvider:ApiKey"] = "", ["Access:PaywallEnabled"] = "false" })));
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var response = await client.PostAsJsonAsync("/api/dumps/process", Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("AI_NOT_CONFIGURED", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }
}
