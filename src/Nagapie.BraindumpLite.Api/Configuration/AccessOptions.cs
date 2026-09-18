namespace Nagapie.BraindumpLite.Api;

public sealed class AccessOptions
{
    public const string SectionName = "Access";
    public bool PaywallEnabled
    {
        get; set;
    }

    public const int FreeDumpLimit = 3;
    public const string PriceDisplay = "€ 4,99";
}
