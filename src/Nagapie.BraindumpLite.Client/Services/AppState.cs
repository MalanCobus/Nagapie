using System.Globalization;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Client.Domain;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Services;

public sealed class AppState(ILocalStorageService storage, INagapieApiClient api, IJSRuntime js)
{
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
            Settings = await storage.ReadAsync<AppSettings>("settings") ?? new();
            SetCulture();
            Store = await storage.ReadAsync<ItemStore>("items") ?? new();
            Categories = await storage.ReadAsync<List<Category>>("categories") ?? Defaults();
            Draft = await storage.ReadAsync<Draft>("draft") ?? new();
            Access = await storage.ReadAsync<AccessState>("access") ?? new();
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
        catch (Exception ex) when (ex is JSException or System.Text.Json.JsonException or InvalidDataException)
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
        await storage.WriteAsync("settings", settings);
        Settings = settings;
        SetCulture();
        await js.InvokeVoidAsync("nagapie.setLanguage", settings.Language);
        Notify();
    }

    public async Task SaveDraftAsync()
    {
        RequireStorage();
        await storage.WriteAsync("draft", Draft);
    }

    public async Task SetReviewAsync(List<BrainDumpItem> items, bool ai)
    {
        var next = Draft with
        {
            Review = items,
            WasAiProcessed = ai
        };
        RequireStorage();
        await storage.WriteAsync("draft", next);
        Draft = next;
    }

    public async Task CommitAsync()
    {
        RequireStorage();
        if (Draft.Review is not { Count: > 0 } || Draft.Review.Any(i => string.IsNullOrWhiteSpace(i.Text) || i.Text.Trim().Length > 5000))
        {
            throw new InvalidDataException(ErrorCodes.InvalidInput);
        }

        if (!Store.SavedDumps.Contains(Draft.Id))
        {
            var next = ThoughtRules.Commit(Store, Draft, Categories, DateTimeOffset.UtcNow);
            await storage.WriteAsync("items", next);
            Store = next;
        }

        await storage.RemoveAsync("draft");
        Draft = new();
        Notify();
    }

    public async Task UpdateAsync(BrainDumpItem item)
    {
        RequireStorage();
        if (string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 5000 || !Horizons.Contains(item.PlanningHorizon))
        {
            throw new InvalidDataException(ErrorCodes.InvalidInput);
        }

        var next = ThoughtRules.Update(Store, item, DateTimeOffset.UtcNow);
        await storage.WriteAsync("items", next);
        Store = next;
        Notify();
    }

    public Task ArchiveAsync(BrainDumpItem item, string reason) => UpdateAsync(item with { CompletionReason = reason, CompletedAtUtc = DateTimeOffset.UtcNow });
    public Task RestoreAsync(BrainDumpItem item) => UpdateAsync(item with { CompletionReason = null, CompletedAtUtc = null, CategoryId = item.CompletionReason == "let-go" ? null : item.CategoryId });
    public async Task DeleteAsync(Guid id)
    {
        RequireStorage();
        var next = Store with
        {
            Items = Store.Items.Where(i => i.Id != id).ToList()
        };
        await storage.WriteAsync("items", next);
        Store = next;
        Notify();
    }

    public async Task SaveCategoryAsync(Category category)
    {
        RequireStorage();
        var next = CategoryRules.Save(Categories, category);
        await storage.WriteAsync("categories", next);
        Categories = next;
        Notify();
    }

    public async Task DeleteCategoryAsync(Guid id)
    {
        RequireStorage();
        if (Categories.Any(c => c.Id == id && c.IsDefault))
        {
            return;
        }

        var nextStore = Store with
        {
            Items = Store.Items.Select(i => i.CategoryId == id ? i with { CategoryId = null } : i).ToList()
        };
        await storage.WriteAsync("items", nextStore);
        Store = nextStore;
        if (Draft.Review is not null)
        {
            foreach (var item in Draft.Review.Where(i => i.CategoryId == id))
            {
                item.CategoryId = null;
            }

            await SaveDraftAsync();
        }

        var next = Categories.Where(c => c.Id != id).ToList();
        await storage.WriteAsync("categories", next);
        Categories = next;
        Notify();
    }

    public async Task UnlockAsync(string token)
    {
        RequireStorage();
        var next = new AccessState
        {
            UnlockToken = token
        };
        await storage.WriteAsync("access", next);
        Access = next;
        Notify();
    }

    public async Task ClearAsync()
    {
        foreach (var key in new[]
        {
            "items",
            "categories",
            "draft",
            "settings",
            "access"
        }

        )
        {
            await storage.RemoveAsync(key);
        }

        Store = new();
        Categories = Defaults();
        Draft = new();
        Settings = new();
        Access = new();
        StorageFailed = false;
        SetCulture();
        Notify();
    }
}
