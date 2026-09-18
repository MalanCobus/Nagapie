using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nagapie.BraindumpLite.Api.Data;
using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Tests;

public partial class RelationalDataTests
{
    [Fact]
    public async Task CommittedRequestCanBeRetriedAfterLostResponseButNotChangedOrReplayedAfterReset()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var draft = new Draft { Text = "Retain once" };
        var item = new BrainDumpItem { Text = "Reviewed", SourceDumpId = draft.Id };
        await SaveDraft(client, draft);
        var before = await Read(client, "items");
        var currentDraft = await Read(client, "draft");
        var request = new SaveThoughtsRequest(before.Version, [new(item, Guid.Empty)], [], draft.Id, currentDraft.Version, Guid.NewGuid());
        var first = await client.PostAsJsonAsync("/api/data/items/changes", request);
        first.EnsureSuccessStatusCode();
        var retry = await client.PostAsJsonAsync("/api/data/items/changes", request);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Single((await Read(client, "items")).Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/data/items/changes",
            request with
            {
                Upserts = [new(item with { Text = "Changed payload" }, Guid.Empty)]
            })).StatusCode);
        (await client.PostAsync("/api/data/clear", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/data/items/changes", request)).StatusCode);
    }

    [Fact]
    public async Task ServerOwnsTimestampsAndLetGoTransitionsAndRejectsContradictoryCompletion()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        var categories = (await Read(client, "categories")).Data!.Value.Deserialize<List<Category>>(JsonSerializerOptions.Web)!;
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        var snapshot = await Commit(client, new BrainDumpItem
        {
            Text = "Release",
            CategoryId = categories.Single(category => category.Key == "let-go").Id,
            CreatedAtUtc = DateTimeOffset.MinValue,
            UpdatedAtUtc = DateTimeOffset.MaxValue
        });
        var item = Assert.Single(snapshot.Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!.Items);
        Assert.Equal("let-go", item.CompletionReason);
        Assert.InRange(item.CreatedAtUtc, start, DateTimeOffset.UtcNow);
        Assert.Equal(item.CreatedAtUtc, item.UpdatedAtUtc);
        Assert.Equal(item.CreatedAtUtc, item.CompletedAtUtc);
        var invalid = item with
        {
            CompletionReason = "completed",
            CompletedAtUtc = null
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/data/items/changes",
            new SaveThoughtsRequest(snapshot.Version, [new(invalid, snapshot.RowVersions![item.Id])], []))).StatusCode);
        var restored = item with
        {
            CompletionReason = null,
            CompletedAtUtc = null
        };
        var response = await client.PostAsJsonAsync("/api/data/items/changes",
            new SaveThoughtsRequest(snapshot.Version, [new(restored, snapshot.RowVersions![item.Id])], [], OperationId: Guid.NewGuid()));
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<UserDocumentResponse>())!.Data!.Value.Deserialize<List<BrainDumpItem>>(JsonSerializerOptions.Web)!.Single();
        Assert.Null(saved.CompletedAtUtc);
        Assert.Null(saved.CategoryId);
        Assert.False(saved.IsCompleted);
        Assert.Equal(item.CreatedAtUtc, saved.CreatedAtUtc);
    }

    [Fact]
    public async Task QueriesAreBoundedFilteredAndDoNotReturnReceiptHistory()
    {
        using var factory = Factory();
        using var client = await AccountTestSupport.CreateUserAsync(factory);
        for (var batch = 0; batch < 2; batch++)
            await Commit(client, Enumerable.Range(batch * 28, 28).Select(index => new BrainDumpItem
            {
                Text = $"Thought {index}",
                PlanningHorizon = index % 2 == 0 ? "today" : "later"
            }).ToArray());
        var first = (await client.GetFromJsonAsync<ThoughtPage>("/api/data/thoughts?PageSize=10&Horizon=today"))!;
        var second = (await client.GetFromJsonAsync<ThoughtPage>("/api/data/thoughts?PageSize=10&Horizon=today&Page=1"))!;
        Assert.Equal(10, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.All(first.Items, item => Assert.Equal("today", item.PlanningHorizon));
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/data/thoughts?PageSize=1000")).StatusCode);
        var legacy = (await Read(client, "items")).Data!.Value.Deserialize<ItemStore>(JsonSerializerOptions.Web)!;
        Assert.Equal(50, legacy.Items.Count);
        Assert.Empty(legacy.SavedDumps);
        Assert.Empty(legacy.AiDumps);
    }

    [Fact]
    public async Task InvalidLegacyAccountDoesNotPreventValidAccountsImporting()
    {
        using var factory = Factory();
        using var first = await AccountTestSupport.CreateUserAsync(factory);
        using var second = await AccountTestSupport.CreateUserAsync(factory);
        var badId = (await AccountTestSupport.RefreshAsync(first)).UserId!;
        var goodId = (await AccountTestSupport.RefreshAsync(second)).UserId!;
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NagapieDbContext>();
        database.UserDocuments.Add(new()
        {
            UserId = badId,
            Key = "items",
            Json = "invalid json"
        });
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => scope.ServiceProvider.GetRequiredService<LegacyDataImporter>().ImportAllAsync(default));
        Assert.True(await database.RelationalAccounts.AnyAsync(row => row.UserId == goodId));
        Assert.False(await database.RelationalAccounts.AnyAsync(row => row.UserId == badId));
        Assert.Equal("invalid json", (await database.UserDocuments.SingleAsync()).Json);
    }
}
