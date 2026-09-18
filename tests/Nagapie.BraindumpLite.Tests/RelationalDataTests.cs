using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Tests;

public class RelationalDataTests
{
    [Fact]
    public async Task PlannedDatePersistsAndInvalidDateCombinationsAreRejected()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var plannedDate = new DateOnly(2027, 3, 28);
        var item = new BrainDumpItem { Text = "Plan a trip", PlanningHorizon = "date", PlannedDate = plannedDate };
        var snapshot = await Commit(client, item);
        var saved = Assert.Single(snapshot.Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!.Items);
        Assert.Equal(plannedDate, saved.PlannedDate);
        Task<HttpResponseMessage> Change(BrainDumpItem value) => client.PostAsJsonAsync("/api/data/items/changes",
            new SaveThoughtsRequest(snapshot.Version, [new(value, snapshot.RowVersions![item.Id])], []));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(item with
        {
            PlannedDate = null
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(item with
        {
            PlanningHorizon = "tomorrow"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(item with
        {
            PlanningHorizon = "next-week",
            PlannedDate = null
        })).StatusCode);
        (await Change(item with
        {
            PlanningHorizon = "tomorrow",
            PlannedDate = null
        })).EnsureSuccessStatusCode();
        var updated = Assert.Single((await Read(client, "items")).Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!.Items);
        Assert.Equal("tomorrow", updated.PlanningHorizon);
        Assert.Null(updated.PlannedDate);
    }

    [Fact]
    public async Task DraftReviewUpgradeMovesNextWeekToLaterAndKeepsChosenDates()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        await Read(client, "draft");
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NagapieDbContext>();
        var draft = await database.Drafts.SingleAsync();
        var date = new DateOnly(2027, 1, 2);
        draft.IsDeleted = false;
        draft.ReviewJson = JsonSerializer.Serialize(new[]
        {
            new BrainDumpItem { Text = "Old planning", PlanningHorizon = "next-week" },
            new BrainDumpItem { Text = "Exact day", PlanningHorizon = "date", PlannedDate = date }
        }, JsonSerializerOptions.Web);
        await database.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<LegacyDataImporter>().ImportAllAsync(default);
        var saved = (await Read(client, "draft")).Data!.Value.Deserialize<Draft>(JsonSerializerOptions.Web)!;
        Assert.Equal("later", saved.Review![0].PlanningHorizon);
        Assert.Equal(date, saved.Review[1].PlannedDate);
    }

    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureServices(AccountTestSupport.UseTestDatabase));
    private static async Task<UserDocumentResponse> Read(HttpClient client, string key) =>
        (await client.GetFromJsonAsync<UserDocumentResponse>("/api/data/" + key))!;
    private static async Task SaveDraft(HttpClient client, Draft draft)
    {
        var current = await Read(client, "draft");
        (await client.PutAsJsonAsync("/api/data/draft", new SaveUserDocumentRequest(
            JsonSerializer.SerializeToElement(draft, JsonSerializerOptions.Web), current.Version))).EnsureSuccessStatusCode();
    }
    private static async Task<UserDocumentResponse> Commit(HttpClient client, params BrainDumpItem[] items)
    {
        var draft = new Draft { Text = "Original text", Review = items.ToList() };
        foreach (var item in items)
            item.SourceDumpId = draft.Id;
        await SaveDraft(client, draft);
        var current = await Read(client, "items");
        var savedDraft = await Read(client, "draft");
        (await client.PostAsJsonAsync("/api/data/items/changes", new SaveThoughtsRequest(current.Version,
            items.Select(item => new RowChange<BrainDumpItem>(item, Guid.Empty)).ToList(), [], draft.Id, savedDraft.Version))).EnsureSuccessStatusCode();
        return await Read(client, "items");
    }

    [Fact]
    public async Task DifferentItemsCanBeEditedFromSameSnapshotButSameItemConflicts()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var first = new BrainDumpItem { Text = "First" };
        var second = new BrainDumpItem { Text = "Second" };
        var snapshot = await Commit(client, first, second);
        Task<HttpResponseMessage> Edit(BrainDumpItem item) => client.PostAsJsonAsync("/api/data/items/changes",
            new SaveThoughtsRequest(snapshot.Version, [new(item, snapshot.RowVersions![item.Id])], []));
        (await Edit(first with
        {
            Text = "Updated first"
        })).EnsureSuccessStatusCode();
        (await Edit(second with
        {
            Text = "Updated second"
        })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Edit(first with
        {
            Text = "Stale overwrite"
        })).StatusCode);
        var result = (await Read(client, "items")).Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!;
        Assert.Contains(result.Items, item => item.Text == "Updated first");
        Assert.Contains(result.Items, item => item.Text == "Updated second");
        (await client.PostAsync("/api/data/clear", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Edit(second)).StatusCode);
    }

    [Fact]
    public async Task CategoryDeletionClearsReferencesAtomicallyAndRejectsOtherUsersCategory()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        using var other = await AccountTestSupport.CreateUserAsync(factory);
        var category = new Category(Guid.NewGuid(), "custom", "Personal", "sage", false);
        var categories = await Read(client, "categories");
        var response = await client.PostAsJsonAsync("/api/data/categories/changes", new SaveCategoriesRequest(categories.Version,
            [new(category, Guid.Empty)], []));
        response.EnsureSuccessStatusCode();
        var categoryVersion = (await response.Content.ReadFromJsonAsync<UserDocumentResponse>())!.RowVersions![category.Id];
        var item = new BrainDumpItem { Text = "Keep me", CategoryId = category.Id };
        await Commit(client, item);
        var otherItem = new BrainDumpItem { Text = "Other" };
        var otherSnapshot = await Commit(other, otherItem);
        var forbidden = await other.PostAsJsonAsync("/api/data/items/changes", new SaveThoughtsRequest(otherSnapshot.Version,
            [new(otherItem with { CategoryId = category.Id }, otherSnapshot.RowVersions![otherItem.Id])], []));
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
        (await client.PostAsJsonAsync("/api/data/categories/changes", new SaveCategoriesRequest(categories.Version,
            [], [new(category.Id, categoryVersion)]))).EnsureSuccessStatusCode();
        var saved = (await Read(client, "items")).Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!;
        Assert.Null(Assert.Single(saved.Items).CategoryId);
        var draft = (await Read(client, "draft")).Data!.Value.Deserialize<Draft>(JsonSerializerOptions.Web)!;
        Assert.Null(Assert.Single(draft.Review!).CategoryId);
    }

    [Fact]
    public async Task StaleDraftCannotCommitAndOldWholeListWritesAreRejected()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var draft = new Draft { Text = "First" };
        await SaveDraft(client, draft);
        var before = await Read(client, "draft");
        await SaveDraft(client, draft with
        {
            Text = "Newer"
        });
        var items = await Read(client, "items");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/data/items/changes",
            new SaveThoughtsRequest(items.Version, [new(new() { Text = "Old", SourceDumpId = draft.Id }, Guid.Empty)], [], draft.Id, before.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/data/items",
            new SaveUserDocumentRequest(JsonSerializer.SerializeToElement(new ItemStore(), JsonSerializerOptions.Web), items.Version))).StatusCode);
        Assert.Empty((await Read(client, "items")).Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!.Items);
    }

    [Fact]
    public async Task InvalidLegacyRelationshipsRollBackImportWithoutChangingArchive()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var userId = (await AccountTestSupport.RefreshAsync(client)).UserId!;
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NagapieDbContext>();
        var json = JsonSerializer.Serialize(new ItemStore { Items = [new() { Text = "Keep original", CategoryId = Guid.NewGuid() }] }, JsonSerializerOptions.Web);
        database.UserDocuments.Add(new()
        {
            UserId = userId,
            Key = "items",
            Json = json
        });
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.ServiceProvider.GetRequiredService<LegacyDataImporter>().ImportAllAsync(default));
        database.ChangeTracker.Clear();
        Assert.Empty(await database.RelationalAccounts.ToListAsync());
        Assert.Empty(await database.Thoughts.ToListAsync());
        Assert.Equal(json, (await database.UserDocuments.SingleAsync()).Json);
    }

    [Fact]
    public async Task HistoryIsOrderedAndPaginatedInTheDatabase()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var userId = (await AccountTestSupport.RefreshAsync(client)).UserId!;
        await Read(client, "items");
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NagapieDbContext>();
        var start = DateTimeOffset.UtcNow;
        database.BrainDumps.AddRange(Enumerable.Range(0, 51).Select(index => new SavedBrainDump
        {
            UserId = userId,
            Id = Guid.NewGuid(),
            Text = index.ToString(),
            SavedAtUtc = start.AddMinutes(index)
        }));
        await database.SaveChangesAsync();
        var first = (await client.GetFromJsonAsync<List<SavedDumpResponse>>("/api/data/history/dumps?page=0"))!;
        var last = (await client.GetFromJsonAsync<List<SavedDumpResponse>>("/api/data/history/dumps?page=1"))!;
        Assert.Equal(50, first.Count);
        Assert.Equal("50", first[0].Text);
        Assert.Equal("0", Assert.Single(last).Text);
    }

    [Fact]
    public async Task LegacyImportPreservesContentReceiptsAndArchiveAndRunsOnlyOnce()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var userId = (await AccountTestSupport.RefreshAsync(client)).UserId!;
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NagapieDbContext>();
        var category = new Category(Guid.NewGuid(), "custom", "Imported", "blue", false);
        var item = new BrainDumpItem { Text = "Saved before upgrade", CategoryId = category.Id, SourceDumpId = Guid.NewGuid(), CompletionReason = "completed", CompletedAtUtc = DateTimeOffset.UtcNow };
        var store = new ItemStore { Items = [item], SavedDumps = [item.SourceDumpId], AiDumps = [item.SourceDumpId] };
        var original = JsonSerializer.Serialize(store, JsonSerializerOptions.Web);
        database.UserDocuments.AddRange(
            new UserDocument { UserId = userId, Key = "items", Json = original },
            new UserDocument { UserId = userId, Key = "categories", Json = JsonSerializer.Serialize(new[] { category }, JsonSerializerOptions.Web) },
            new UserDocument { UserId = userId, Key = "draft", Json = JsonSerializer.Serialize(new Draft { Text = "Unfinished", Review = [item] }, JsonSerializerOptions.Web) });
        await database.SaveChangesAsync();
        var importer = scope.ServiceProvider.GetRequiredService<LegacyDataImporter>();
        await importer.ImportAllAsync(default);
        await importer.ImportAllAsync(default);
        var imported = await database.Thoughts.SingleAsync();
        Assert.Equal(item, imported.ToContractForTest());
        var dump = await database.BrainDumps.SingleAsync();
        Assert.True(dump.IsCommitted && dump.WasAiProcessed);
        Assert.False(dump.OriginalAvailable);
        Assert.Equal(original, (await database.UserDocuments.SingleAsync(row => row.Key == "items")).Json);
        Assert.Equal("Unfinished", (await database.Drafts.SingleAsync()).Text);
        (await client.PostAsync("/api/data/clear", null)).EnsureSuccessStatusCode();
        await importer.ImportAllAsync(default);
        Assert.Empty(await database.Thoughts.AsNoTracking().ToListAsync());
        Assert.All(await database.UserDocuments.AsNoTracking().ToListAsync(), document => Assert.Null(document.Json));
    }
}

internal static class ThoughtAssertions
{
    internal static BrainDumpItem ToContractForTest(this Thought row) => new()
    {
        Id = row.Id,
        SourceDumpId = row.SourceDumpId ?? Guid.Empty,
        CategoryId = row.CategoryId,
        Text = row.Text,
        PlanningHorizon = row.PlanningHorizon,
        PlannedDate = row.PlannedDate,
        InputMethod = row.InputMethod,
        CompletionReason = row.CompletionReason,
        CreatedAtUtc = row.CreatedAtUtc,
        UpdatedAtUtc = row.UpdatedAtUtc,
        CompletedAtUtc = row.CompletedAtUtc
    };
}
