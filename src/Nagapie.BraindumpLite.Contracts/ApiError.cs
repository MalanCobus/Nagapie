namespace Nagapie.BraindumpLite.Contracts;

public sealed record ApiError(string Code, string CorrelationId);
