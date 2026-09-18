namespace Nagapie.BraindumpLite.Api.Data;

public sealed class RelationalAccount
{
    public string UserId { get; set; } = "";
    // A reset changes this token so stale clients cannot restore cleared data.
    public Guid Epoch { get; set; } = Guid.NewGuid();
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int SuccessfulAiDumps
    {
        get; set;
    }
    public string? LicenseHash
    {
        get; set;
    }
}
