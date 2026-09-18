using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nagapie.BraindumpLite.Api;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Tests;

public partial class ApiTests
{
    private sealed class ValidLicense : ILicenseVerifier
    {
        public Task<VerifyLicenseResponse> VerifyAsync(string license, CancellationToken cancellationToken) =>
            Task.FromResult(new VerifyLicenseResponse(true, "old-unbound-token"));
    }

    [Fact]
    public async Task LicenseBelongsToOneAccountAndForgedAccessDocumentCannotGrantAccess()
    {
        using var factory = Factory(true).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ILicenseVerifier, ValidLicense>()));
        using var first = await AccountTestSupport.CreateUserAsync(factory);
        using var second = await AccountTestSupport.CreateUserAsync(factory);
        (await first.PostAsJsonAsync("/api/licenses/verify", new VerifyLicenseRequest("one-license"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await second.PostAsJsonAsync("/api/licenses/verify", new VerifyLicenseRequest("one-license"))).StatusCode);
        (await second.PutAsJsonAsync("/api/data/access", new SaveUserDocumentRequest(
            JsonSerializer.SerializeToElement(new AccessState { UnlockToken = "account-entitlement" }, JsonSerializerOptions.Web), Guid.Empty))).EnsureSuccessStatusCode();
        var access = (await second.GetFromJsonAsync<UserDocumentResponse>("/api/data/access"))!.Data!.Value.Deserialize<AccessState>(JsonSerializerOptions.Web)!;
        Assert.Null(access.UnlockToken);
        var owner = (await first.GetFromJsonAsync<UserDocumentResponse>("/api/data/access"))!.Data!.Value.Deserialize<AccessState>(JsonSerializerOptions.Web)!;
        Assert.Equal("account-entitlement", owner.UnlockToken);
    }

    [Fact]
    public async Task AiCommitCountsServerProcessingEvenWhenBrowserDeniesItAndRetryDoesNotCountTwice()
    {
        using var factory = Factory(true);
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var request = Request();
        (await client.PostAsJsonAsync("/api/dumps/process", request)).EnsureSuccessStatusCode();
        var draft = new Draft { Id = request.SourceDumpId, Text = request.Text, WasAiProcessed = false };
        var beforeDraft = (await client.GetFromJsonAsync<UserDocumentResponse>("/api/data/draft"))!;
        var save = await client.PutAsJsonAsync("/api/data/draft", new SaveUserDocumentRequest(
            JsonSerializer.SerializeToElement(draft, JsonSerializerOptions.Web), beforeDraft.Version));
        save.EnsureSuccessStatusCode();
        var draftVersion = (await save.Content.ReadFromJsonAsync<UserDocumentResponse>())!.Version;
        var page = (await client.GetFromJsonAsync<ThoughtPage>("/api/data/thoughts"))!;
        var commit = new SaveThoughtsRequest(page.Epoch, [new(new() { Text = "Reviewed", SourceDumpId = draft.Id }, Guid.Empty)], [], draft.Id, draftVersion, Guid.NewGuid());
        (await client.PostAsJsonAsync("/api/data/items/changes", commit)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/data/items/changes", commit)).EnsureSuccessStatusCode();
        Assert.Equal(1, (await client.GetFromJsonAsync<ThoughtPage>("/api/data/thoughts"))!.SuccessfulAiDumps);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NagapieDbContext>();
        Assert.True((await database.BrainDumps.SingleAsync()).WasAiProcessed);
    }
}
