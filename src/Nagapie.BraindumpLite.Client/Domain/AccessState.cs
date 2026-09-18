namespace Nagapie.BraindumpLite.Client.Domain;

public sealed record AccessState
{
    public string? UnlockToken
    {
        get; set;
    }
}
