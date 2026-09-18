namespace Nagapie.BraindumpLite.Api.Data;

public sealed class SavedBrainDump
{
    public string UserId { get; set; } = "";
    public Guid Id
    {
        get; set;
    }
    public string Text { get; set; } = "";
    public string InputMethod { get; set; } = "text";
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

