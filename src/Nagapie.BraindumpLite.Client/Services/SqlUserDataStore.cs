using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Services;

// Version tokens and pending requests live only for the current signed-in session.
public sealed class SqlUserDataStore(HttpClient http) : IUserDataStore
{
    private readonly Dictionary<string, Guid> versions = [];
    private readonly Dictionary<Guid, Guid> thoughts = [], categories = [];
    private readonly SemaphoreSlim writes = new(1, 1);
    private SaveThoughtsRequest? pending;

    public Task<AppSettings> ReadSettingsAsync() => ReadAsync<AppSettings>("settings");
    public Task SaveSettingsAsync(AppSettings settings) => WriteAsync("settings", settings);
    public Task<AccessState> ReadAccessAsync() => ReadAsync<AccessState>("access");
    public Task SaveAccessAsync(AccessState access) => WriteAsync("access", access);
    public Task<Draft> ReadDraftAsync() => ReadAsync<Draft>("draft");
    public Task SaveDraftAsync(Draft draft) => WriteAsync("draft", draft);

    private async Task<T> ReadAsync<T>(string key) where T : class, new()
    {
        using var response = await http.GetAsync("api/data/" + key);
        var document = await ReadResponseAsync(response);
        versions[key] = document.Version;
        return document.Data?.Deserialize<T>(JsonSerializerOptions.Web) ?? new();
    }

    private async Task WriteAsync<T>(string key, T value) => await LockedAsync(async () =>
    {
        if (!versions.ContainsKey(key))
            await ReadAsync<object>(key);
        using var response = await http.PutAsJsonAsync("api/data/" + key,
            new SaveUserDocumentRequest(JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web), versions[key]));
        versions[key] = (await ReadResponseAsync(response)).Version;
    });

    public async Task DeleteDraftAsync() => await LockedAsync(async () =>
    {
        if (!versions.ContainsKey("draft"))
            await ReadDraftAsync();
        using var response = await http.DeleteAsync($"api/data/draft?version={versions["draft"]}");
        versions["draft"] = (await ReadResponseAsync(response)).Version;
    });

    public async Task<List<Category>> ReadCategoriesAsync()
    {
        using var response = await http.GetAsync("api/data/categories");
        var document = await ReadResponseAsync(response);
        versions["categories"] = document.Version;
        categories.Clear();
        foreach (var pair in document.RowVersions ?? [])
            categories[pair.Key] = pair.Value;
        return document.Data?.Deserialize<List<Category>>(JsonSerializerOptions.Web) ?? CategoryRules.CreateDefaults();
    }

    public Task SaveCategoryAsync(Category category) => ChangeCategoryAsync(category, null);
    public Task DeleteCategoryAsync(Guid id) => ChangeCategoryAsync(null, id);
    private async Task ChangeCategoryAsync(Category? category, Guid? delete) => await LockedAsync(async () =>
    {
        if (!versions.ContainsKey("categories"))
            await ReadCategoriesAsync();
        using var response = await http.PostAsJsonAsync("api/data/categories/changes", new SaveCategoriesRequest(
            versions["categories"], category is null ? [] : [new(category, categories.GetValueOrDefault(category.Id))],
            delete is { } id ? [new(id, categories.GetValueOrDefault(id))] : []));
        var saved = await ReadResponseAsync(response);
        foreach (var pair in saved.RowVersions ?? [])
            categories[pair.Key] = pair.Value;
        if (delete is { } removed)
            categories.Remove(removed);
    });

    public async Task<ThoughtPage> QueryThoughtsAsync(ThoughtQuery query)
    {
        var url = $"api/data/thoughts?Page={query.Page}&PageSize={query.PageSize}&Unsorted={query.Unsorted}";
        if (query.CategoryId is { } category)
            url += $"&CategoryId={category}";
        if (query.Completed is { } completed)
            url += $"&Completed={completed}";
        if (query.Horizon is { } horizon)
            url += "&Horizon=" + Uri.EscapeDataString(horizon);
        using var response = await http.GetAsync(url);
        await RequireSuccessAsync(response);
        var page = await response.Content.ReadFromJsonAsync<ThoughtPage>() ?? throw new ApiClientException(ErrorCodes.Storage);
        if (versions.GetValueOrDefault("items") != page.Epoch)
            thoughts.Clear();
        versions["items"] = page.Epoch;
        foreach (var pair in page.RowVersions)
            thoughts[pair.Key] = pair.Value;
        return page;
    }

    public async Task<List<BrainDumpItem>> CommitDraftAsync(Draft draft) => await ChangeThoughtsAsync(
        (draft.Review ?? []).Select(item => item with { SourceDumpId = draft.Id }).ToList(), null, draft.Id);

    public async Task<BrainDumpItem> UpdateThoughtAsync(BrainDumpItem item) =>
        (await ChangeThoughtsAsync([item], null, null)).Single();

    public async Task DeleteThoughtAsync(Guid id) => await ChangeThoughtsAsync([], id, null);

    private async Task<List<BrainDumpItem>> ChangeThoughtsAsync(List<BrainDumpItem> items, Guid? delete, Guid? commit)
    {
        List<BrainDumpItem> savedItems = [];
        await LockedAsync(async () =>
        {
            if (!versions.ContainsKey("items"))
                await QueryThoughtsAsync(new());
            if (commit is not null && !versions.ContainsKey("draft"))
                await ReadDraftAsync();
            var request = new SaveThoughtsRequest(versions["items"],
                items.Select(item => new RowChange<BrainDumpItem>(item, commit is null ? thoughts.GetValueOrDefault(item.Id) : Guid.Empty)).ToList(),
                delete is { } id ? [new(id, thoughts.GetValueOrDefault(id))] : [], commit,
                commit is null ? Guid.Empty : versions["draft"]);
            // Reuse the exact request after an uncertain network result.
            if (pending is not null && JsonSerializer.Serialize(pending with
            {
                OperationId = default
            }) == JsonSerializer.Serialize(request))
                request = pending;
            else
                request = request with
                {
                    OperationId = commit ?? Guid.NewGuid()
                };
            pending = request;
            using var response = await http.PostAsJsonAsync("api/data/items/changes", request);
            var saved = await ReadResponseAsync(response);
            foreach (var pair in saved.RowVersions ?? [])
                thoughts[pair.Key] = pair.Value;
            if (delete is { } removed)
                thoughts.Remove(removed);
            savedItems = saved.Data?.Deserialize<List<BrainDumpItem>>(JsonSerializerOptions.Web) ?? items;
            pending = null;
        });
        return savedItems;
    }

    public async Task ClearAsync() => await LockedAsync(async () =>
    {
        using var response = await http.PostAsync("api/data/clear", null);
        await RequireSuccessAsync(response);
        versions.Clear();
        thoughts.Clear();
        categories.Clear();
        pending = null;
    });

    private async Task LockedAsync(Func<Task> action)
    {
        await writes.WaitAsync();
        try
        {
            await action();
        }
        finally { writes.Release(); }
    }

    private static async Task<UserDocumentResponse> ReadResponseAsync(HttpResponseMessage response)
    {
        await RequireSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<UserDocumentResponse>() ?? throw new ApiClientException(ErrorCodes.Storage);
    }

    private static async Task RequireSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ApiClientException(ErrorCodes.SessionChanged);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new ApiClientException(ErrorCodes.SaveConflict);
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            throw new ApiClientException(error?.Code ?? ErrorCodes.Storage);
        }
        catch (JsonException) { throw new ApiClientException(ErrorCodes.Storage); }
    }
}
