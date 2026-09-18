using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Tests;

public class UserAccountsTests
{
    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureServices(AccountTestSupport.UseTestDatabase));

    private static Task<HttpResponseMessage> SaveAsync<T>(HttpClient client, string key, T value, Guid version = default) =>
        client.PutAsJsonAsync("/api/data/" + key,
            new SaveUserDocumentRequest(JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web), version));

    [Fact]
    public async Task AnonymousUsersCannotReadWriteOrUseAi()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/data/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SaveAsync(client, "settings", new AppSettings())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/dumps/process", new
        {
        })).StatusCode);
    }

    [Fact]
    public async Task EachUserHasIsolatedDataAndCannotChooseAnotherOwner()
    {
        using var factory = Factory();
        using var first = await AccountTestSupport.CreateUserAsync(factory);
        using var second = await AccountTestSupport.CreateUserAsync(factory);
        var firstSession = await AccountTestSupport.RefreshAsync(first);
        (await SaveAsync(first, "draft", new Draft { Text = "Private first user thought" })).EnsureSuccessStatusCode();
        Assert.Null((await second.GetFromJsonAsync<UserDocumentResponse>("/api/data/draft"))!.Data);
        second.DefaultRequestHeaders.Remove("X-Account-Id");
        second.DefaultRequestHeaders.Add("X-Account-Id", firstSession.UserId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/data/draft")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SaveAsync(second, "draft", new Draft { Text = "Replace" })).StatusCode);
    }

    [Fact]
    public async Task DataSurvivesLogoutAndLoginInAnotherSession()
    {
        using var factory = Factory();
        using var first = await AccountTestSupport.CreateUserAsync(factory, "owner@example.com");
        (await SaveAsync(first, "settings", new AppSettings { Language = "en-US" })).EnsureSuccessStatusCode();
        (await first.PostAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/data/settings")).StatusCode);
        using var second = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        await AccountTestSupport.RefreshAsync(second);
        (await second.PostAsJsonAsync("/api/account/login", new CredentialsRequest("owner@example.com", "A long test password!"))).EnsureSuccessStatusCode();
        await AccountTestSupport.RefreshAsync(second);
        var saved = await second.GetFromJsonAsync<UserDocumentResponse>("/api/data/settings");
        Assert.Equal("en-US", saved!.Data!.Value.Deserialize<AppSettings>(JsonSerializerOptions.Web)!.Language);
    }

    [Fact]
    public async Task MutationsRequireAntiforgeryToken()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(client, "settings", new AppSettings())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/data/clear", null)).StatusCode);
    }

    [Fact]
    public async Task StaleSavesCannotOverwriteNewerDataOrRecreateDeletedData()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var first = await SaveAsync(client, "draft", new Draft { Text = "Newer text" });
        first.EnsureSuccessStatusCode();
        var version = (await first.Content.ReadFromJsonAsync<UserDocumentResponse>())!.Version;
        Assert.Equal(HttpStatusCode.Conflict, (await SaveAsync(client, "draft", new Draft { Text = "Old text" })).StatusCode);
        (await client.DeleteAsync($"/api/data/draft?version={version}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await SaveAsync(client, "draft", new Draft { Text = "Restore old" }, version)).StatusCode);
    }

    [Fact]
    public async Task CommitPreservesOriginalDumpAndClearOnlyAffectsCurrentUser()
    {
        using var factory = Factory();
        using var first = await AccountTestSupport.CreateUserAsync(factory);
        using var second = await AccountTestSupport.CreateUserAsync(factory);
        var draft = new Draft { Text = "Original raw dump" };
        (await SaveAsync(first, "draft", draft)).EnsureSuccessStatusCode();
        (await SaveAsync(first, "items", new ItemStore
        {
            Items = [new() { Text = "Reviewed task", SourceDumpId = draft.Id }],
            SavedDumps = [draft.Id]
        })).EnsureSuccessStatusCode();
        var history = await first.GetFromJsonAsync<List<SavedDumpResponse>>("/api/data/history/dumps");
        Assert.Equal(draft.Text, Assert.Single(history!).Text);
        Assert.Empty((await second.GetFromJsonAsync<List<SavedDumpResponse>>("/api/data/history/dumps"))!);
        (await SaveAsync(second, "draft", new Draft { Text = "Keep my data" })).EnsureSuccessStatusCode();
        (await first.PostAsync("/api/data/clear", null)).EnsureSuccessStatusCode();
        Assert.Empty((await first.GetFromJsonAsync<List<SavedDumpResponse>>("/api/data/history/dumps"))!);
        Assert.NotNull((await second.GetFromJsonAsync<UserDocumentResponse>("/api/data/draft"))!.Data);
    }

    [Fact]
    public async Task InvalidDataAndUnknownKeysAreRejected()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(client, "settings", new
        {
            language = "invalid"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveAsync(client, "another-user", new
        {
        })).StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsWeakPasswordsAndDuplicateAccounts()
    {
        using var factory = Factory();
        using var existing = await AccountTestSupport.CreateUserAsync(factory, "duplicate@example.com");
        using var client = factory.CreateClient();
        await AccountTestSupport.RefreshAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/register",
            new CredentialsRequest("weak@example.com", "short"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/register",
            new CredentialsRequest("duplicate@example.com", "A long test password!"))).StatusCode);
        Assert.Null((await AccountTestSupport.RefreshAsync(client)).UserId);
    }

    [Fact]
    public async Task RepeatedWrongPasswordsLockAccountTemporarily()
    {
        using var factory = Factory();
        using var existing = await AccountTestSupport.CreateUserAsync(factory, "locked@example.com");
        using var client = factory.CreateClient();
        await AccountTestSupport.RefreshAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/login",
                new CredentialsRequest("locked@example.com", "Wrong long password!"))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account/login",
            new CredentialsRequest("locked@example.com", "A long test password!"))).StatusCode);
    }
}
