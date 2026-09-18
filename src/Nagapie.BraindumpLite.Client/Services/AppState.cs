using System.Globalization;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Client.Domain;
using Nagapie.BraindumpLite.Contracts;
namespace Nagapie.BraindumpLite.Client.Services;
public sealed class AppState(ILocalStorageService storage, HttpClient http, IJSRuntime js)
{
    public AppSettings Settings { get; private set; } = new();
    public ItemStore Store { get; private set; } = new();
    public List<Category> Categories { get; private set; } = [];
    public Draft Draft { get; private set; } = new();
    public AccessState Access { get; private set; } = new();
    public PublicConfiguration Config { get; private set; } = new(false, 3, "€ 4,99", null, false, "OpenAI", "https://openai.com/policies/privacy-policy/");
    public bool Loaded { get; private set; }
    public bool StorageFailed { get; private set; }
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
    public static readonly string[] Horizons = ["today", "next-week", "later"];
    public async Task InitializeAsync()
    {
        if (Loaded) return;
        try {
            Settings = await storage.ReadAsync<AppSettings>("settings") ?? new();
            SetCulture();
            Store = await storage.ReadAsync<ItemStore>("items") ?? new();
            Categories = await storage.ReadAsync<List<Category>>("categories") ?? Defaults();
            Draft = await storage.ReadAsync<Draft>("draft") ?? new();
            Access = await storage.ReadAsync<AccessState>("access") ?? new();
            ValidateStoredData();
            var ids = Categories.Select(c => c.Id).ToHashSet();
            foreach (var item in Store.Items.Concat(Draft.Review ?? [])) if (item.CategoryId is not null && !ids.Contains(item.CategoryId.Value)) item.CategoryId = null;
        } catch (Exception ex) when (ex is JSException or System.Text.Json.JsonException or InvalidDataException) { StorageFailed = true; }
        try {
            if (await js.InvokeAsync<bool>("nagapie.online")) {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                Config = await http.GetFromJsonAsync<PublicConfiguration>("api/config", timeout.Token) ?? Config;
            }
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or JSException) { }
        Loaded = true;
        Notify();
    }
    private void ValidateStoredData()
    {
        if (Settings.Language is not ("nl-NL" or "en-US") || Store.Items is null || Store.SavedDumps is null || Store.AiDumps is null || Categories is null || Categories.Count > 25 || Draft.Text is null || Draft.Text.Length > 5000 || Categories.Any(c => c is null || c.Id == Guid.Empty || c.Key is null || c.ColorToken is not ("sage" or "blue" or "yellow" or "terra" or "stone") || (!c.IsDefault && (string.IsNullOrWhiteSpace(c.CustomName) || c.CustomName.Length > 30))) || Categories.Select(c => c.Id).Distinct().Count() != Categories.Count || Store.Items.Concat(Draft.Review ?? []).Any(i => i is null || i.Id == Guid.Empty || string.IsNullOrWhiteSpace(i.Text) || i.Text.Length > 5000 || !Horizons.Contains(i.PlanningHorizon) || i.CompletionReason is not (null or "completed" or "let-go"))) throw new InvalidDataException("STORAGE_INVALID");
    }
    public static List<Category> Defaults() => new[] { ("do", "sage"), ("plan", "blue"), ("idea", "yellow"), ("remember", "terra"), ("let-go", "stone") }.Select((c, i) => new Category(new Guid($"00000000-0000-0000-0000-{i + 1:000000000000}"), c.Item1, null, c.Item2, true)).ToList();
    private void SetCulture()
    {
        var culture = CultureInfo.GetCultureInfo(Settings.Language is "en-US" ? "en-US" : "nl-NL");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
    private void RequireStorage() { if (StorageFailed) throw new InvalidDataException("STORAGE_INVALID"); }
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        RequireStorage();
        await storage.WriteAsync("settings", settings);
        Settings = settings;
        SetCulture();
        await js.InvokeVoidAsync("nagapie.setLanguage", settings.Language);
        Notify();
    }
    public async Task SaveDraftAsync() { RequireStorage(); await storage.WriteAsync("draft", Draft); }
    public async Task SetReviewAsync(List<BrainDumpItem> items, bool ai)
    {
        var next = Draft with { Review = items, WasAiProcessed = ai };
        RequireStorage();
        await storage.WriteAsync("draft", next);
        Draft = next;
    }
    public async Task CommitAsync()
    {
        RequireStorage();
        if (Draft.Review is not { Count: > 0 } || Draft.Review.Any(i => string.IsNullOrWhiteSpace(i.Text) || i.Text.Trim().Length > 5000)) throw new InvalidDataException("INVALID_INPUT");
        if (!Store.SavedDumps.Contains(Draft.Id)) {
            var next = Store with { Items = [..Store.Items], SavedDumps = [..Store.SavedDumps], AiDumps = [..Store.AiDumps] };
            foreach (var review in Draft.Review) {
                var letGo = Categories.Any(c => c.Id == review.CategoryId && c.Key == "let-go");
                next.Items.Add(review with { Text = review.Text.Trim(), SourceDumpId = Draft.Id, CompletionReason = letGo ? "let-go" : null, CompletedAtUtc = letGo ? DateTimeOffset.UtcNow : null });
            }
            next.SavedDumps.Add(Draft.Id);
            if (Draft.WasAiProcessed) next.AiDumps.Add(Draft.Id);
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
        if (string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 5000 || !Horizons.Contains(item.PlanningHorizon)) throw new InvalidDataException("INVALID_INPUT");
        var next = Store with { Items = Store.Items.Select(i => i.Id == item.Id ? item with { Text = item.Text.Trim(), UpdatedAtUtc = DateTimeOffset.UtcNow } : i).ToList() };
        await storage.WriteAsync("items", next); Store = next; Notify();
    }
    public Task ArchiveAsync(BrainDumpItem item, string reason) => UpdateAsync(item with { CompletionReason = reason, CompletedAtUtc = DateTimeOffset.UtcNow });
    public Task RestoreAsync(BrainDumpItem item) => UpdateAsync(item with { CompletionReason = null, CompletedAtUtc = null, CategoryId = item.CompletionReason == "let-go" ? null : item.CategoryId });
    public async Task DeleteAsync(Guid id)
    {
        RequireStorage();
        var next = Store with { Items = Store.Items.Where(i => i.Id != id).ToList() };
        await storage.WriteAsync("items", next); Store = next; Notify();
    }
    public async Task SaveCategoryAsync(Category category)
    {
        RequireStorage();
        if (category.IsDefault || string.IsNullOrWhiteSpace(category.CustomName) || category.CustomName.Trim().Length > 30 || Categories.Any(c => c.Id != category.Id && string.Equals(c.CustomName, category.CustomName.Trim(), StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("CATEGORY_INVALID");
        var next = Categories.Where(c => c.Id != category.Id).Append(category with { CustomName = category.CustomName.Trim() }).ToList();
        if (next.Count > 25) throw new InvalidDataException("CATEGORY_INVALID");
        await storage.WriteAsync("categories", next); Categories = next; Notify();
    }
    public async Task DeleteCategoryAsync(Guid id)
    {
        RequireStorage();
        if (Categories.Any(c => c.Id == id && c.IsDefault)) return;
        var nextStore = Store with { Items = Store.Items.Select(i => i.CategoryId == id ? i with { CategoryId = null } : i).ToList() };
        await storage.WriteAsync("items", nextStore); Store = nextStore;
        if (Draft.Review is not null) { foreach (var item in Draft.Review.Where(i => i.CategoryId == id)) item.CategoryId = null; await SaveDraftAsync(); }
        var next = Categories.Where(c => c.Id != id).ToList();
        await storage.WriteAsync("categories", next); Categories = next; Notify();
    }
    public async Task UnlockAsync(string token) { RequireStorage(); var next = new AccessState { UnlockToken = token }; await storage.WriteAsync("access", next); Access = next; Notify(); }
    public async Task ClearAsync()
    {
        foreach (var key in new[] { "items", "categories", "draft", "settings", "access" }) await storage.RemoveAsync(key);
        Store = new(); Categories = Defaults(); Draft = new(); Settings = new(); Access = new(); StorageFailed = false; SetCulture(); Notify();
    }
}
