namespace Nagapie.BraindumpLite.Contracts.Domain;

public sealed record BrainDumpItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceDumpId
    {
        get; set;
    }
    public string Text { get; set; } = "";
    public Guid? CategoryId
    {
        get; set;
    }
    public string PlanningHorizon { get; set; } = "later";
    public string InputMethod { get; set; } = "text";
    public string? CompletionReason
    {
        get; set;
    }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc
    {
        get; set;
    }
    public bool IsCompleted => CompletionReason is not null;
}
