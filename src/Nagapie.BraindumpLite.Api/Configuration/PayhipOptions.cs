namespace Nagapie.BraindumpLite.Api;

public sealed class PayhipOptions
{
    public const string SectionName = "Payhip";
    public string? ProductSecret
    {
        get; set;
    }
    public string? ProductUrl
    {
        get; set;
    }
}
