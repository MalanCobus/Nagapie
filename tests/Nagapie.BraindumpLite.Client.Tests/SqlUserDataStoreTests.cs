using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Tests;

public class SqlUserDataStoreTests
{
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

        await Assert.ThrowsAsync<ApiClientException>(() => store.WriteAsync("draft", new Draft { Text = "Keep this" }));
        await store.WriteAsync("draft", new Draft { Text = "Keep this" });
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
            new SqlUserDataStore(http).WriteAsync("settings", new AppSettings()));
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
        var draft = await new SqlUserDataStore(http).ReadAsync<Draft>("draft");
        Assert.Equal("SQL data", draft!.Text);
    }
}
