namespace Nagapie.BraindumpLite.Contracts.Domain;

public static class PlanningHorizons
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "today", "next-week", "later" });
}
