using System.Globalization;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Services;

public sealed class AppState(IUserDataStore storage, INagapieApiClient api, IJSRuntime js)
{
    private readonly SemaphoreSlim mutations = new(1, 1);
    public ThoughtQuery Query { get; private set; } = new();
    public bool HasMoreThoughts
    {
        get; private set;
    }
    public int SuccessfulAiDumps
    {
        get; private set;
    }
    private Guid? committedDraftId;
    public bool DraftCommitted
    {
        get => committedDraftId == Draft.Id;
        private set => committedDraftId = value ? Draft.Id : null;
    }

    private async Task MutateAsync(Func<Task> action)
    {
        await mutations.WaitAsync();
        try
        {
            await action();
        }
        finally { mutations.Release(); }
    }

    public Task LoadThoughtsAsync(ThoughtQuery query) => MutateAsync(async () =>
    {
        await LoadThoughtsCoreAsync(query);
        Notify();
    });

    private async Task LoadThoughtsCoreAsync(ThoughtQuery query)
    {
        var page = await storage.QueryThoughtsAsync(query);
        Query = query;
        Store = new()
        {
            Items = page.Items
        };
        HasMoreThoughts = page.HasMore;
        SuccessfulAiDumps = page.SuccessfulAiDumps;
        DraftCommitted = page.DraftCommitted;
    }

    public AppSettings Settings { get; private set; } = new();
    public ItemStore Store { get; private set; } = new();
    public List<Category> Categories { get; private set; } = [];
    public Draft Draft { get; private set; } = new();
    public AccessState Access { get; private set; } = new();
    public PublicConfiguration Config { get; private set; } = new(false, 3, "€ 4,99", null, false, "OpenAI", "https://openai.com/policies/privacy-policy/");
    public bool Loaded
    {
        get; private set;
    }
    public bool StorageFailed
    {
        get; private set;
    }

    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
    public static IReadOnlyList<string> Horizons => PlanningHorizons.All;

    public async Task InitializeAsync()
    {
        if (Loaded)
        {
            return;
        }

        try
        {
            Settings = await storage.ReadSettingsAsync();
            SetCulture();
            Draft = await storage.ReadDraftAsync();
            await LoadThoughtsCoreAsync(Query);
            Categories = await storage.ReadCategoriesAsync();
            Access = await storage.ReadAccessAsync();
            StoredDataValidator.Validate(Settings, Store, Categories, Draft);
            var ids = Categories.Select(c => c.Id).ToHashSet();
            foreach (var item in Store.Items.Concat(Draft.Review ?? []))
            {
                if (item.CategoryId is not null && !ids.Contains(item.CategoryId.Value))
                {
                    item.CategoryId = null;
                }
            }
        }
        catch (Exception ex) when (ex is JSException or System.Text.Json.JsonException or InvalidDataException or ApiClientException or HttpRequestException or TaskCanceledException)
        {
            StorageFailed = true;
        }

        try
        {
            if (await js.InvokeAsync<bool>("nagapie.online"))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                Config = await api.GetConfigurationAsync(timeout.Token) ?? Config;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or JSException)
        {
        }

        Loaded = true;
        Notify();
    }

    public static List<Category> Defaults() => CategoryRules.CreateDefaults();
    private void SetCulture()
    {
        var culture = CultureInfo.GetCultureInfo(Settings.Language is "en-US" ? "en-US" : "nl-NL");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void RequireStorage()
    {
        if (StorageFailed)
        {
            throw new InvalidDataException(ErrorCodes.StorageInvalid);
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        RequireStorage();
        await storage.SaveSettingsAsync(settings);
        Settings = settings;
        SetCulture();
        await js.InvokeVoidAsync("nagapie.setLanguage", settings.Language);
        Notify();
    }

    public Task SaveDraftAsync() => MutateAsync(() => SaveDraftCoreAsync());

    private async Task SaveDraftCoreAsync()
    {
        RequireStorage();
        await storage.SaveDraftAsync(Draft);
    }

    public Task SetReviewAsync(List<BrainDumpItem> items, bool ai) => MutateAsync(() => SetReviewCoreAsync(items, ai));

    private async Task SetReviewCoreAsync(List<BrainDumpItem> items, bool ai)
    {
        var next = Draft with
        {
            Review = items,
            WasAiProcessed = ai
        };
        RequireStorage();
        await storage.SaveDraftAsync(next);
        Draft = next;
    }

    public Task CommitAsync() => MutateAsync(() => CommitCoreAsync());

    private async Task CommitCoreAsync()
    {
        RequireStorage();
        if (Draft.Review is not { Count: > 0 } || Draft.Review.Any(i => string.IsNullOrWhiteSpace(i.Text) || i.Text.Trim().Length > 5000))
        {
            throw new InvalidDataException(ErrorCodes.InvalidInput);
        }

        if (!DraftCommitted)
        {
            var saved = await storage.CommitDraftAsync(Draft);
            Store = Store with
            {
                Items = [.. Store.Items, .. saved]
            };
            DraftCommitted = true;
        }

        await storage.DeleteDraftAsync();
        Draft = new();
        DraftCommitted = false;
        await LoadThoughtsCoreAsync(Query);
        Notify();
    }

    public Task UpdateAsync(BrainDumpItem item) => MutateAsync(() => UpdateCoreAsync(item));

    private async Task UpdateCoreAsync(BrainDumpItem item)
    {
        RequireStorage();
        if (string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 5000 || !PlanningHorizons.IsValid(item))
        {
            throw new InvalidDataException(ErrorCodes.InvalidInput);
        }

        var saved = await storage.UpdateThoughtAsync(item);
        var next = Store with
        {
            Items = Store.Items.Select(existing => existing.Id == saved.Id ? saved : existing).ToList()
        };
        Store = next;
        Notify();
    }

    public Task ArchiveAsync(BrainDumpItem item, string reason) => UpdateAsync(item with { CompletionReason = reason, CompletedAtUtc = DateTimeOffset.UtcNow });
    public Task RestoreAsync(BrainDumpItem item) => UpdateAsync(item with { CompletionReason = null, CompletedAtUtc = null, CategoryId = item.CompletionReason == "let-go" ? null : item.CategoryId });
    public Task DeleteAsync(Guid id) => MutateAsync(() => DeleteCoreAsync(id));

    private async Task DeleteCoreAsync(Guid id)
    {
        RequireStorage();
        var next = Store with
        {
            Items = Store.Items.Where(i => i.Id != id).ToList()
        };
        await storage.DeleteThoughtAsync(id);
        Store = next;
        Notify();
    }

    public Task SaveCategoryAsync(Category category) => MutateAsync(() => SaveCategoryCoreAsync(category));

    private async Task SaveCategoryCoreAsync(Category category)
    {
        RequireStorage();
        var next = CategoryRules.Save(Categories, category);
        await storage.SaveCategoryAsync(category);
        Categories = next;
        Notify();
    }

    public Task DeleteCategoryAsync(Guid id) => MutateAsync(() => DeleteCategoryCoreAsync(id));

    private async Task DeleteCategoryCoreAsync(Guid id)
    {
        RequireStorage();
        if (Categories.Any(c => c.Id == id && c.IsDefault))
        {
            return;
        }

        var next = Categories.Where(c => c.Id != id).ToList();
        // The server clears item and draft references in the same transaction as the category deletion.
        await storage.DeleteCategoryAsync(id);
        Categories = next;
        await LoadThoughtsCoreAsync(Query);
        Draft = await storage.ReadDraftAsync();
        Notify();
    }

    public async Task UnlockAsync(string token)
    {
        RequireStorage();
        var next = new AccessState
        {
            UnlockToken = token
        };
        await storage.SaveAccessAsync(next);
        Access = next;
        Notify();
    }

    public Task ClearAsync() => MutateAsync(() => ClearCoreAsync());

    private async Task ClearCoreAsync()
    {
        await storage.ClearAsync();
        Store = new();
        Categories = Defaults();
        Draft = new();
        Settings = new();
        Access = new();
        StorageFailed = false;
        Query = new();
        await LoadThoughtsCoreAsync(Query);
        Access = await storage.ReadAccessAsync();
        SetCulture();
        Notify();
    }
    public void ResetMemory()
    {
        Store = new();
        Categories = Defaults();
        Draft = new();
        Settings = new();
        Access = new();
        Loaded = false;
        StorageFailed = false;
        Query = new();
        HasMoreThoughts = false;
        SuccessfulAiDumps = 0;
        DraftCommitted = false;
    }
}
