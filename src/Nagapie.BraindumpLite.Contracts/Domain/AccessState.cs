namespace Nagapie.BraindumpLite.Contracts.Domain;

public sealed record AccessState
{
    public string? UnlockToken
    {
        get; set;
    }
}
