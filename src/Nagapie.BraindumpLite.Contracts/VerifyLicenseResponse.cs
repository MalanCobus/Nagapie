namespace Nagapie.BraindumpLite.Contracts;

public sealed record VerifyLicenseResponse(bool IsValid, string? UnlockToken);
