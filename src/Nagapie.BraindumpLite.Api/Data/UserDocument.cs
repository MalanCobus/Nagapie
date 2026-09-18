namespace Nagapie.BraindumpLite.Api.Data;

public sealed class UserDocument
{
    public string UserId { get; set; } = "";
    public string Key { get; set; } = "";
    public string? Json
    {
        get; set;
    }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

