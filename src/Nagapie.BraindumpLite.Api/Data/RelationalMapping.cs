using System.Text.Json;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

internal static class RelationalMapping
{
    internal static Thought ToRow(this BrainDumpItem item, string userId) => new()
    {
        UserId = userId,
        Id = item.Id,
        SourceDumpId = item.SourceDumpId == Guid.Empty ? null : item.SourceDumpId,
        CategoryId = item.CategoryId,
        Text = item.Text,
        PlanningHorizon = item.PlanningHorizon,
        InputMethod = item.InputMethod,
        CompletionReason = item.CompletionReason,
        CreatedAtUtc = item.CreatedAtUtc,
        UpdatedAtUtc = item.UpdatedAtUtc,
        CompletedAtUtc = item.CompletedAtUtc
    };
    internal static BrainDumpItem ToContract(this Thought item) => new()
    {
        Id = item.Id,
        SourceDumpId = item.SourceDumpId ?? Guid.Empty,
        CategoryId = item.CategoryId,
        Text = item.Text,
        PlanningHorizon = item.PlanningHorizon,
        InputMethod = item.InputMethod,
        CompletionReason = item.CompletionReason,
        CreatedAtUtc = item.CreatedAtUtc,
        UpdatedAtUtc = item.UpdatedAtUtc,
        CompletedAtUtc = item.CompletedAtUtc
    };
    internal static UserCategory ToRow(this Category category, string userId) => new()
    {
        UserId = userId,
        Id = category.Id,
        Key = category.Key,
        CustomName = category.CustomName,
        ColorToken = category.ColorToken,
        IsDefault = category.IsDefault
    };
    internal static Category ToContract(this UserCategory category) =>
        new(category.Id, category.Key, category.CustomName, category.ColorToken, category.IsDefault);
    internal static Draft ToContract(this UserDraft draft) => new()
    {
        Id = draft.Id,
        Text = draft.Text,
        InputMethod = draft.InputMethod,
        WasAiProcessed = draft.WasAiProcessed,
        Review = draft.ReviewJson is null ? null : JsonSerializer.Deserialize<List<BrainDumpItem>>(draft.ReviewJson, JsonSerializerOptions.Web)
    };
    internal static void SetDraft(this UserDraft row, Draft draft)
    {
        row.Id = draft.Id;
        row.Text = draft.Text;
        row.InputMethod = draft.InputMethod;
        row.WasAiProcessed = draft.WasAiProcessed;
        row.ReviewJson = draft.Review is null ? null : JsonSerializer.Serialize(draft.Review, JsonSerializerOptions.Web);
        row.IsDeleted = false;
    }
}
