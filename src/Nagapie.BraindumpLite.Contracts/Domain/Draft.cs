namespace Nagapie.BraindumpLite.Contracts.Domain;

public sealed record Draft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public string InputMethod { get; set; } = "text";
    public bool WasAiProcessed
    {
        get; set;
    }
    public List<BrainDumpItem>? Review
    {
        get; set;
    }
}
