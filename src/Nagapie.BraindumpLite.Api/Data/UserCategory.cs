namespace Nagapie.BraindumpLite.Api.Data;

public sealed class UserCategory
{
    public string UserId { get; set; } = "";
    public Guid Id
    {
        get; set;
    }
    public string Key { get; set; } = "";
    public string? CustomName
    {
        get; set;
    }
    public string ColorToken { get; set; } = "sage";
    public bool IsDefault
    {
        get; set;
    }
    public Guid Version { get; set; } = Guid.NewGuid();
}
