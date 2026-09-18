namespace Nagapie.BraindumpLite.Contracts.Domain;

public static class PlanningHorizons
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "today", "tomorrow", "later", "date" });

    public static bool IsValid(BrainDumpItem item) => All.Contains(item.PlanningHorizon) &&
        (item.PlanningHorizon == "date" ? item.PlannedDate is not null : item.PlannedDate is null);

    public static bool UpgradeLegacy(IEnumerable<BrainDumpItem> items)
    {
        var changed = false;
        foreach (var item in items.Where(item => item.PlanningHorizon == "next-week"))
        {
            item.PlanningHorizon = "later";
            item.PlannedDate = null;
            changed = true;
        }
        return changed;
    }
}
