namespace Nagapie.BraindumpLite.Client.Domain;
public sealed record BrainDumpItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceDumpId { get; set; }
    public string Text { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public string PlanningHorizon { get; set; } = "later";
    public string InputMethod { get; set; } = "text";
    public string? CompletionReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public bool IsCompleted => CompletionReason is not null;
}
public sealed record Category(Guid Id, string Key, string? CustomName, string ColorToken, bool IsDefault);
public sealed record AppSettings
{
    public string Language { get; set; } = "nl-NL";
    public bool HasCompletedOnboarding { get; set; }
    public bool HasConsentedToAiProcessing { get; set; }
    public bool HasAcknowledgedSpeech { get; set; }
}
public sealed record Draft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public string InputMethod { get; set; } = "text";
    public bool WasAiProcessed { get; set; }
    public List<BrainDumpItem>? Review { get; set; }
}
public sealed record ItemStore
{
    public List<BrainDumpItem> Items { get; set; } = [];
    // Commit receipts and items share one atomic storage write. Retries never double count.
    public HashSet<Guid> SavedDumps { get; set; } = [];
    public HashSet<Guid> AiDumps { get; set; } = [];
}
public sealed record AccessState { public string? UnlockToken { get; set; } }
public sealed record StorageEnvelope<T>(int SchemaVersion, T Data);
