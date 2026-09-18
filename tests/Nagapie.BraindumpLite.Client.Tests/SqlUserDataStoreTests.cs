using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Tests;

public class SqlUserDataStoreTests
{
    [Fact]
    public async Task LostCommitResponseRetriesTheExactOperationAndUsesServerTimestamps()
    {
        var epoch = Guid.NewGuid();
        var draftVersion = Guid.NewGuid();
        var draft = new Draft { Text = "Original", Review = [new() { Text = "Reviewed" }] };
        var requests = new List<string>();
        var authoritative = draft.Review[0] with
        {
            SourceDumpId = draft.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = request.RequestUri!.AbsolutePath.EndsWith("draft")
                        ? JsonContent.Create(new UserDocumentResponse(JsonSerializer.SerializeToElement(draft), draftVersion))
                        : JsonContent.Create(new ThoughtPage([], epoch, [], false, 0, false))
                };
            requests.Add(await request.Content!.ReadAsStringAsync());
            if (requests.Count == 1)
                throw new HttpRequestException("Response lost after commit");
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UserDocumentResponse(JsonSerializer.SerializeToElement(new[] { authoritative }), epoch,
                    new()
                    {
                        [authoritative.Id] = Guid.NewGuid()
                    }))
            };
        }))
        {
            BaseAddress = new("http://localhost")
        };
        var store = new SqlUserDataStore(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => store.CommitDraftAsync(draft));
        var saved = Assert.Single(await store.CommitDraftAsync(draft));
        Assert.Equal(requests[0], requests[1]);
        Assert.Equal(authoritative.CreatedAtUtc, saved.CreatedAtUtc);
    }

    [Fact]
    public async Task ConcurrentCategoryCommandsNeverInferDeletionFromMissingCollectionMembers()
    {
        var sent = new List<SaveCategoriesRequest>();
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new UserDocumentResponse(null, Guid.NewGuid(), []))
                };
            var change = (await request.Content!.ReadFromJsonAsync<SaveCategoriesRequest>())!;
            sent.Add(change);
            await Task.Yield();
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UserDocumentResponse(null, change.Epoch,
                change.Upserts.ToDictionary(row => row.Value.Id, _ => Guid.NewGuid())))
            };
        }))
        {
            BaseAddress = new("http://localhost")
        };
        var store = new SqlUserDataStore(http);
        await Task.WhenAll(store.SaveCategoryAsync(new(Guid.NewGuid(), "custom", "First", "sage", false)),
            store.SaveCategoryAsync(new(Guid.NewGuid(), "custom", "Second", "blue", false)));
        Assert.Equal(2, sent.Count);
        Assert.All(sent, request => { Assert.Single(request.Upserts); Assert.Empty(request.Deletes); });
    }

    [Fact]
    public async Task EditingOneThoughtSendsOnlyThatThoughtAndKeepsVersionsForUnchangedItems()
    {
        var first = new BrainDumpItem { Text = "First" };
        var second = new BrainDumpItem { Text = "Second" };
        var firstVersion = Guid.NewGuid();
        var secondVersion = Guid.NewGuid();
        var updatedVersion = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var requests = new List<SaveThoughtsRequest>();
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ThoughtPage([first, second], epoch,
                    new()
                    {
                        [first.Id] = firstVersion,
                        [second.Id] = secondVersion
                    }, false, 0, false))
                };
            Assert.Equal("/api/data/items/changes", request.RequestUri!.AbsolutePath);
            var change = (await request.Content!.ReadFromJsonAsync<SaveThoughtsRequest>())!;
            requests.Add(change);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UserDocumentResponse(null, epoch,
                change.Upserts.ToDictionary(row => row.Value.Id, _ => updatedVersion)))
            };
        }))
        {
            BaseAddress = new Uri("http://localhost")
        };
        var storage = new SqlUserDataStore(http);
        var initial = await storage.QueryThoughtsAsync(new());
        var edited = initial with
        {
            Items = [first with { Text = "Edited" }, second]
        };
        await storage.UpdateThoughtAsync(edited.Items[0]);
        var sent = Assert.Single(requests[0].Upserts);
        Assert.Equal(first.Id, sent.Value.Id);
        Assert.Equal(firstVersion, sent.Version);
        await storage.UpdateThoughtAsync(second with
        {
            Text = "Other edit"
        });
        Assert.Equal(secondVersion, Assert.Single(requests[1].Upserts).Version);
        Assert.All(requests, request => Assert.Empty(request.Deletes));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    [Fact]
    public async Task FailedWriteKeepsOriginalVersionForRetry()
    {
        var version = Guid.NewGuid();
        var writes = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new UserDocumentResponse(null, version))
                };
            }

            var payload = await request.Content!.ReadFromJsonAsync<SaveUserDocumentRequest>();
            Assert.Equal(version, payload!.Version);
            writes++;
            return writes == 1
                ? new(HttpStatusCode.ServiceUnavailable)
                {
                    Content = JsonContent.Create(new ApiError("STORAGE", "test"))
                }
                : new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new UserDocumentResponse(null, Guid.NewGuid()))
                };
        }))
        {
            BaseAddress = new Uri("http://localhost")
        };
        var store = new SqlUserDataStore(http);

        await Assert.ThrowsAsync<ApiClientException>(() => store.SaveDraftAsync(new Draft { Text = "Keep this" }));
        await store.SaveDraftAsync(new Draft { Text = "Keep this" });
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task ConflictsAreReportedAndNotRetriedOverNewerData()
    {
        var writes = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new UserDocumentResponse(null, Guid.NewGuid()))
                });
            }

            writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
        }))
        {
            BaseAddress = new Uri("http://localhost")
        };
        var error = await Assert.ThrowsAsync<ApiClientException>(() =>
            new SqlUserDataStore(http).SaveSettingsAsync(new AppSettings()));
        Assert.Equal("SAVE_CONFLICT", error.Code);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task FreshStoreReadsSavedDataFromServer()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new UserDocumentResponse(
                JsonSerializer.SerializeToElement(new Draft { Text = "SQL data" }, JsonSerializerOptions.Web), Guid.NewGuid()))
        })))
        {
            BaseAddress = new Uri("http://localhost")
        };
        var draft = await new SqlUserDataStore(http).ReadDraftAsync();
        Assert.Equal("SQL data", draft!.Text);
    }
}
