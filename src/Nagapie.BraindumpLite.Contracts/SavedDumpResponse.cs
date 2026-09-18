namespace Nagapie.BraindumpLite.Contracts;

public sealed record SavedDumpResponse(Guid Id, string Text, string InputMethod, DateTimeOffset SavedAtUtc);

