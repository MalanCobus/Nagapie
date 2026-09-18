namespace Nagapie.BraindumpLite.Contracts.Domain;

public sealed record Category(Guid Id, string Key, string? CustomName, string ColorToken, bool IsDefault);
