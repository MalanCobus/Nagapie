using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Services;

// Only in-memory version tokens are kept in the browser. SQL owns all persisted data.
public sealed class SqlUserDataStore(HttpClient http) : IUserDataStore
{
    private readonly Dictionary<string, Guid> versions = [];
    private readonly SemaphoreSlim writes = new(1, 1);

    public async Task<T?> ReadAsync<T>(string key)
    {
        var document = await ReadDocumentAsync(key);
        return document.Data is { } data ? data.Deserialize<T>(JsonSerializerOptions.Web) : default;
    }

    private async Task<UserDocumentResponse> ReadDocumentAsync(string key)
    {
        using var response = await http.GetAsync("api/data/" + Uri.EscapeDataString(key));
        await RequireSuccessAsync(response);
        var document = await response.Content.ReadFromJsonAsync<UserDocumentResponse>()
            ?? throw new ApiClientException("STORAGE");
        versions[key] = document.Version;
        return document;
    }

    public async Task WriteAsync<T>(string key, T value)
    {
        await writes.WaitAsync();
        try
        {
            if (!versions.ContainsKey(key))
            {
                await ReadDocumentAsync(key);
            }

            var data = JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);
            using var response = await http.PutAsJsonAsync("api/data/" + Uri.EscapeDataString(key),
                new SaveUserDocumentRequest(data, versions[key]));
            await RecordVersionAsync(key, response);
        }
        finally { writes.Release(); }
    }

    public async Task RemoveAsync(string key)
    {
        await writes.WaitAsync();
        try
        {
            if (!versions.ContainsKey(key))
            {
                await ReadDocumentAsync(key);
            }

            using var response = await http.DeleteAsync($"api/data/{Uri.EscapeDataString(key)}?version={versions[key]}");
            await RecordVersionAsync(key, response);
        }
        finally { writes.Release(); }
    }

    public async Task ClearAsync()
    {
        await writes.WaitAsync();
        try
        {
            using var response = await http.PostAsync("api/data/clear", null);
            await RequireSuccessAsync(response);
            versions.Clear();
        }
        finally { writes.Release(); }
    }

    private async Task RecordVersionAsync(string key, HttpResponseMessage response)
    {
        await RequireSuccessAsync(response);
        var document = await response.Content.ReadFromJsonAsync<UserDocumentResponse>()
            ?? throw new ApiClientException("STORAGE");
        versions[key] = document.Version;
    }

    private static async Task RequireSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiClientException(ErrorCodes.SessionChanged);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new ApiClientException(ErrorCodes.SaveConflict);
        }

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            throw new ApiClientException(error?.Code ?? "STORAGE");
        }
        catch (JsonException) { throw new ApiClientException("STORAGE"); }
    }
}

