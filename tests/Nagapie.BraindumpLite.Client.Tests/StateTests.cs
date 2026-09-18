using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Client.Services;
using Nagapie.BraindumpLite.Contracts.Domain;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Tests;

public class StateTests
{
    private sealed class MemoryStorage : IUserDataStore
    {
        public Task<AppSettings> ReadSettingsAsync() => ReadOrNewAsync<AppSettings>("settings");
        public Task SaveSettingsAsync(AppSettings value) => WriteAsync("settings", value);
        public Task<AccessState> ReadAccessAsync() => ReadOrNewAsync<AccessState>("access");
        public Task SaveAccessAsync(AccessState value) => WriteAsync("access", value);
        public Task<Draft> ReadDraftAsync() => ReadOrNewAsync<Draft>("draft");
        public Task SaveDraftAsync(Draft value) => WriteAsync("draft", value);
        public Task DeleteDraftAsync() => RemoveAsync("draft");
        public async Task<List<Category>> ReadCategoriesAsync() => await ReadAsync<List<Category>>("categories") ?? CategoryRules.CreateDefaults();
        public async Task SaveCategoryAsync(Category category)
        {
            await Task.Yield();
            await WriteAsync("categories", CategoryRules.Save(await ReadCategoriesAsync(), category));
        }
        public async Task DeleteCategoryAsync(Guid id) => await WriteAsync("categories", (await ReadCategoriesAsync()).Where(category => category.Id != id).ToList());
        public async Task<ThoughtPage> QueryThoughtsAsync(ThoughtQuery query)
        {
            var store = await ReadOrNewAsync<ItemStore>("items");
            var draft = await ReadDraftAsync();
            return new(store.Items, Guid.Empty, [], false, store.AiDumps.Count, store.SavedDumps.Contains(draft.Id));
        }
        public async Task<List<BrainDumpItem>> CommitDraftAsync(Draft draft)
        {
            var store = await ReadOrNewAsync<ItemStore>("items");
            var next = ThoughtRules.Commit(store, draft, await ReadCategoriesAsync(), DateTimeOffset.UtcNow);
            await WriteAsync("items", next);
            return next.Items.Where(item => item.SourceDumpId == draft.Id).ToList();
        }
        public async Task<BrainDumpItem> UpdateThoughtAsync(BrainDumpItem item)
        {
            await WriteAsync("items", ThoughtRules.Update(await ReadOrNewAsync<ItemStore>("items"), item, DateTimeOffset.UtcNow));
            return item;
        }
        public async Task DeleteThoughtAsync(Guid id)
        {
            var store = await ReadOrNewAsync<ItemStore>("items");
            await WriteAsync("items", store with
            {
                Items = store.Items.Where(item => item.Id != id).ToList()
            });
        }
        public async Task ClearAsync()
        {
            foreach (var key in new[] { "items", "categories", "draft", "settings", "access" })
                await RemoveAsync(key);
        }
        private async Task<T> ReadOrNewAsync<T>(string key) where T : class, new() => await ReadAsync<T>(key) ?? new();
        public Dictionary<string, string> Data = [];
        public string? FailWrite, FailRemove;
        public Task<T?> ReadAsync<T>(string key) => Task.FromResult(Data.TryGetValue(key, out var value) ? JsonSerializer.Deserialize<T>(value) : default);
        public Task WriteAsync<T>(string key, T value)
        {
            if (FailWrite == key)
            {
                throw new JSException("quota exceeded");
            }

            if (key == "categories" && value is List<Category> categories && Data.TryGetValue("items", out var itemsJson))
            {
                var items = JsonSerializer.Deserialize<ItemStore>(itemsJson)!;
                foreach (var item in items.Items.Where(item => item.CategoryId is { } id && !categories.Any(category => category.Id == id)))
                    item.CategoryId = null;
                Data["items"] = JsonSerializer.Serialize(items);
            }
            Data[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            if (FailRemove == key)
            {
                throw new JSException("remove failed");
            }

            Data.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class Js : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string id, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string id, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(id, args);
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"paywallEnabled":false,"freeDumpLimit":3,"priceDisplay":"€ 4,99","aiAvailable":false,"providerName":"OpenAI","providerPrivacyUrl":"https://openai.com"}""", Encoding.UTF8, "application/json") });
    }

    private static async Task<AppState> Create(MemoryStorage storage)
    {
        var state = new AppState(storage, new NagapieApiClient(new HttpClient(new Offline()) { BaseAddress = new("http://localhost") }), new Js());
        await state.InitializeAsync();
        return state;
    }

    private static async Task Review(AppState state, bool ai = true)
    {
        state.Draft.Text = "One. Two.";
        await state.SetReviewAsync([new() { Text = "One", SourceDumpId = state.Draft.Id }, new() { Text = "Two", SourceDumpId = state.Draft.Id }], ai);
    }

    [Fact]
    public async Task ConcurrentCategorySavesPreserveBothCategoriesInMemoryAndStorage()
    {
        var storage = new MemoryStorage();
        var state = await Create(storage);
        var first = new Category(Guid.NewGuid(), "custom", "First", "sage", false);
        var second = new Category(Guid.NewGuid(), "custom", "Second", "blue", false);
        await Task.WhenAll(state.SaveCategoryAsync(first), state.SaveCategoryAsync(second));
        Assert.Contains(first, state.Categories);
        Assert.Contains(second, state.Categories);
        Assert.Equal(state.Categories, await storage.ReadCategoriesAsync());
    }

    [Fact]
    public async Task CountOnlyIncreasesAfterSuccessfulWrite()
    {
        var storage = new MemoryStorage();
        var state = await Create(storage);
        await Review(state);
        storage.FailWrite = "items";
        await Assert.ThrowsAsync<JSException>(state.CommitAsync);
        Assert.Empty(state.Store.Items);
        Assert.Equal(0, state.SuccessfulAiDumps);
        Assert.NotNull(state.Draft.Review);
        storage.FailWrite = null;
        await state.CommitAsync();
        Assert.Equal(2, state.Store.Items.Count);
        Assert.Equal(1, state.SuccessfulAiDumps);
        Assert.Null(state.Draft.Review);
        Assert.Single(state.Store.Items.Select(i => i.SourceDumpId).Distinct());
    }

    [Fact]
    public async Task InterruptedCommitCanBeRetriedWithoutDuplicates()
    {
        var storage = new MemoryStorage();
        var state = await Create(storage);
        await Review(state);
        storage.FailRemove = "draft";
        await Assert.ThrowsAsync<JSException>(state.CommitAsync);
        Assert.Equal(2, state.Store.Items.Count);
        var refreshed = await Create(storage);
        storage.FailRemove = null;
        await refreshed.CommitAsync();
        Assert.Equal(2, refreshed.Store.Items.Count);
        Assert.Equal(1, refreshed.SuccessfulAiDumps);
    }

    [Fact]
    public async Task ManualSaveDoesNotConsumeFreeAiSession()
    {
        var state = await Create(new());
        await Review(state, false);
        await state.CommitAsync();
        Assert.Equal(0, state.SuccessfulAiDumps);
    }

    [Fact]
    public async Task StartingNewDraftAfterInterruptedCleanupDoesNotReuseOldCommitStatus()
    {
        var storage = new MemoryStorage();
        var state = await Create(storage);
        await Review(state);
        storage.FailRemove = "draft";
        await Assert.ThrowsAsync<JSException>(state.CommitAsync);
        Assert.True(state.DraftCommitted);
        state.Draft.Id = Guid.NewGuid();
        Assert.False(state.DraftCommitted);
        await Review(state);
        storage.FailRemove = null;
        await state.CommitAsync();
        Assert.Equal(4, state.Store.Items.Count);
        Assert.Equal(2, state.SuccessfulAiDumps);
    }

    [Fact]
    public async Task LetGoIsRecoverableAndRestorationClearsCategory()
    {
        var state = await Create(new());
        var id = state.Categories.Single(c => c.Key == "let-go").Id;
        await state.SetReviewAsync([new() { Text = "Release this", CategoryId = id }], false);
        await state.CommitAsync();
        var item = Assert.Single(state.Store.Items);
        Assert.Equal("let-go", item.CompletionReason);
        await state.RestoreAsync(item);
        Assert.False(state.Store.Items[0].IsCompleted);
        Assert.Null(state.Store.Items[0].CategoryId);
    }

    [Fact]
    public async Task FailedEditLeavesOriginalUntouched()
    {
        var storage = new MemoryStorage();
        var state = await Create(storage);
        await Review(state);
        await state.CommitAsync();
        storage.FailWrite = "items";
        var item = state.Store.Items[0];
        await Assert.ThrowsAsync<JSException>(() => state.UpdateAsync(item with { Text = "Changed" }));
        Assert.Equal("One", state.Store.Items[0].Text);
    }

    [Fact]
    public async Task CategoryDeletionPreservesThoughts()
    {
        var state = await Create(new());
        var c = new Category(Guid.NewGuid(), "custom", "Personal", "sage", false);
        await state.SaveCategoryAsync(c);
        await state.SetReviewAsync([new() { Text = "Keep me", CategoryId = c.Id }], false);
        await state.CommitAsync();
        await state.DeleteCategoryAsync(c.Id);
        Assert.Null(Assert.Single(state.Store.Items).CategoryId);
        Assert.DoesNotContain(state.Categories, category => category.Id == c.Id);
        await state.DeleteCategoryAsync(state.Categories[0].Id);
        Assert.Equal(5, state.Categories.Count);
    }

    [Fact]
    public async Task CustomNamesAreUniqueIgnoringCase()
    {
        var state = await Create(new());
        await state.SaveCategoryAsync(new(Guid.NewGuid(), "custom", "Personal", "sage", false));
        await Assert.ThrowsAsync<InvalidDataException>(() => state.SaveCategoryAsync(new(Guid.NewGuid(), "custom", "personal", "sage", false)));
    }

    [Fact]
    public async Task DeleteRemovesOnlySelectedItem()
    {
        var state = await Create(new());
        await Review(state);
        await state.CommitAsync();
        await state.DeleteAsync(state.Store.Items[0].Id);
        Assert.Equal("Two", Assert.Single(state.Store.Items).Text);
    }

    [Fact]
    public async Task ClearLeavesUnrelatedKeysAlone()
    {
        var storage = new MemoryStorage();
        storage.Data["other-app"] = "keep";
        var state = await Create(storage);
        await Review(state);
        await state.CommitAsync();
        await state.ClearAsync();
        Assert.Equal("keep", storage.Data["other-app"]);
        Assert.Empty(state.Store.Items);
    }

    [Fact]
    public async Task CorruptDataIsNeverOverwritten()
    {
        var storage = new MemoryStorage();
        storage.Data["items"] = "broken";
        var state = await Create(storage);
        Assert.True(state.StorageFailed);
        await Assert.ThrowsAsync<InvalidDataException>(state.SaveDraftAsync);
        Assert.Equal("broken", storage.Data["items"]);
    }

    [Fact]
    public async Task LanguagesHaveMatchingResourcesAndSwitchImmediately()
    {
        var state = await Create(new());
        var localizer = new AppLocalizer(state);
        Assert.Equal("Laat het maar los.", localizer["Dump_Title"].Value);
        var nl = localizer.GetAllStrings(true).Select(s => s.Name).Order().ToList();
        await state.SaveSettingsAsync(state.Settings with
        {
            Language = "en-US"
        });
        Assert.Equal("Let it all out.", localizer["Dump_Title"].Value);
        Assert.Equal(nl, localizer.GetAllStrings(true).Select(s => s.Name).Order().ToList());
    }

    [Fact]
    public void CalendarEscapesAndUsesExclusiveAllDayEnd()
    {
        var ics = CalendarExport.Create("Hello, world;\\\nNext", "Idea", new(2026, 9, 18), new(2026, 9, 19), true);
        Assert.Contains("DTSTART;VALUE=DATE:20260918\r\nDTEND;VALUE=DATE:20260919", ics);
        Assert.Contains("SUMMARY:Hello\\, world\\;\\\\\\nNext", ics);
        Assert.EndsWith("\r\n", ics);
    }

    [Fact]
    public void CalendarFoldsUtf8WithoutSplittingCharacters()
    {
        var ics = CalendarExport.Create(new string('é', 100) + "🌱", "", new(2026, 9, 18, 9, 0, 0, DateTimeKind.Utc), new(2026, 9, 18, 9, 30, 0, DateTimeKind.Utc), false);
        Assert.All(ics.Split("\r\n"), line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75));
        Assert.Contains("DTSTART:20260918T090000Z", ics);
    }
}
