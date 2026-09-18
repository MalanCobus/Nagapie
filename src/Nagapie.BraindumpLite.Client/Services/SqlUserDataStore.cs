using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Services;

// Only in-memory version tokens are kept in the browser. SQL owns all persisted data.
public sealed class SqlUserDataStore(HttpClient http) : IUserDataStore
{
    private readonly Dictionary<string, Guid> versions = [];
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly Dictionary<string, UserDocumentResponse> snapshots = [];

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
        snapshots[key] = document;
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
            if (key is "items" or "categories")
            {
                await SaveChangesAsync(key, data);
                return;
            }
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
            snapshots.Clear();
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

    private async Task SaveChangesAsync(string key, JsonElement data)
    {
        var snapshot = snapshots[key];
        var rowVersions = snapshot.RowVersions ?? [];
        HttpResponseMessage response;
        List<Guid> removed;
        if (key == "items")
        {
            var before = snapshot.Data?.Deserialize<ItemStore>(JsonSerializerOptions.Web) ?? new();
            var after = data.Deserialize<ItemStore>(JsonSerializerOptions.Web)!;
            var original = before.Items.ToDictionary(item => item.Id);
            var updates = after.Items.Where(item => !original.TryGetValue(item.Id, out var old) || old != item)
                .Select(item => new RowChange<BrainDumpItem>(item, rowVersions.GetValueOrDefault(item.Id))).ToList();
            var remaining = after.Items.Select(item => item.Id).ToHashSet();
            removed = before.Items.Where(item => !remaining.Contains(item.Id)).Select(item => item.Id).ToList();
            var commits = after.SavedDumps.Except(before.SavedDumps).ToList();
            if (commits.Count > 1)
                throw new ApiClientException(ErrorCodes.InvalidInput);
            response = await http.PostAsJsonAsync("api/data/items/changes", new SaveThoughtsRequest(snapshot.Version, updates,
                removed.Select(id => new RowDelete(id, rowVersions[id])).ToList(), commits.Count == 0 ? null : commits[0],
                versions.GetValueOrDefault("draft")));
        }
        else
        {
            var before = snapshot.Data?.Deserialize<List<Category>>(JsonSerializerOptions.Web) ?? [];
            var after = data.Deserialize<List<Category>>(JsonSerializerOptions.Web)!;
            var original = before.ToDictionary(category => category.Id);
            var updates = after.Where(category => !original.TryGetValue(category.Id, out var old) || old != category)
                .Select(category => new RowChange<Category>(category, rowVersions.GetValueOrDefault(category.Id))).ToList();
            var remaining = after.Select(category => category.Id).ToHashSet();
            removed = before.Where(category => !remaining.Contains(category.Id)).Select(category => category.Id).ToList();
            response = await http.PostAsJsonAsync("api/data/categories/changes", new SaveCategoriesRequest(snapshot.Version, updates,
                removed.Select(id => new RowDelete(id, rowVersions[id])).ToList()));
        }
        using (response)
        {
            await RequireSuccessAsync(response);
            var saved = await response.Content.ReadFromJsonAsync<UserDocumentResponse>() ?? throw new ApiClientException(ErrorCodes.Storage);
            var nextVersions = new Dictionary<Guid, Guid>(rowVersions);
            foreach (var id in removed)
                nextVersions.Remove(id);
            foreach (var pair in saved.RowVersions ?? [])
                nextVersions[pair.Key] = pair.Value;
            snapshots[key] = new(data.Clone(), saved.Version, nextVersions);
            versions[key] = saved.Version;
        }
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
