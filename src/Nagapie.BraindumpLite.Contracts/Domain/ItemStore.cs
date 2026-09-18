namespace Nagapie.BraindumpLite.Contracts.Domain;

public sealed record ItemStore
{
    public List<BrainDumpItem> Items { get; set; } = [];
    // Commit receipts and items share one atomic storage write. Retries never double count.
    public HashSet<Guid> SavedDumps { get; set; } = [];
    public HashSet<Guid> AiDumps { get; set; } = [];
}
