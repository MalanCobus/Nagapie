namespace Nagapie.BraindumpLite.Contracts;

public sealed record SplitItemDto(string Text, Guid? SuggestedCategoryId, string PlanningHorizon);
