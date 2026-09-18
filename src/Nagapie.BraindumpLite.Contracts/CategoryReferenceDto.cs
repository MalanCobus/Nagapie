namespace Nagapie.BraindumpLite.Contracts;

public sealed record CategoryReferenceDto(Guid Id, string Key, string DisplayName, bool IsDefault);
