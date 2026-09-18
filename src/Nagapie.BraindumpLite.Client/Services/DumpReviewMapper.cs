using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Services;

public static class DumpReviewMapper
{
    public static List<BrainDumpItem> Map(
        ProcessDumpResponse response,
        IReadOnlyCollection<Category> categories,
        string inputMethod)
    {
        var categoryIds = categories.Select(category => category.Id).ToHashSet();

        return response.Items.Select(item => new BrainDumpItem
        {
            Text = item.Text,
            SourceDumpId = response.SourceDumpId,
            CategoryId = item.SuggestedCategoryId is { } id && categoryIds.Contains(id) ? id : null,
            PlanningHorizon = item.PlanningHorizon is "today" or "tomorrow" or "later"
                ? item.PlanningHorizon
                : "later",
            InputMethod = inputMethod
        }).ToList();
    }
}
