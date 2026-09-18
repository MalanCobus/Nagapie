namespace Nagapie.BraindumpLite.Contracts;

public sealed record PublicConfiguration(bool PaywallEnabled, int FreeDumpLimit, string PriceDisplay, string? PayhipProductUrl, bool AiAvailable, string ProviderName, string ProviderPrivacyUrl);
