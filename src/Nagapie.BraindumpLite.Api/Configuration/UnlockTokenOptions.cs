namespace Nagapie.BraindumpLite.Api;

public sealed class UnlockTokenOptions
{
    public const string SectionName = "UnlockTokens";
    public string SigningKey { get; set; } = "";
}
