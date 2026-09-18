namespace Nagapie.BraindumpLite.Api.Data;

public sealed class Thought
{
    public string UserId { get; set; } = "";
    public Guid Id
    {
        get; set;
    }
    public Guid? SourceDumpId
    {
        get; set;
    }
    public Guid? CategoryId
    {
        get; set;
    }
    public string Text { get; set; } = "";
    public string PlanningHorizon { get; set; } = "later";
    public string InputMethod { get; set; } = "text";
    public string? CompletionReason
    {
        get; set;
    }
    public DateTimeOffset CreatedAtUtc
    {
        get; set;
    }
    public DateTimeOffset UpdatedAtUtc
    {
        get; set;
    }
    public DateTimeOffset? CompletedAtUtc
    {
        get; set;
    }
    public Guid Version { get; set; } = Guid.NewGuid();
}
