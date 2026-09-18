namespace Nagapie.BraindumpLite.Contracts;
public sealed record CategoryReferenceDto(Guid Id, string Key, string DisplayName, bool IsDefault);
public sealed record ProcessDumpRequest(Guid SourceDumpId, string Text, string Language, string InputMethod, List<CategoryReferenceDto> AvailableCategories, string? UnlockToken, int SuccessfulDumpCount = 0);
public sealed record SplitItemDto(string Text, Guid? SuggestedCategoryId, string PlanningHorizon);
public sealed record ProcessDumpResponse(Guid SourceDumpId, List<SplitItemDto> Items, string PromptVersion);
public sealed record ApiError(string Code, string CorrelationId);
public sealed record VerifyLicenseRequest(string LicenseKey);
public sealed record VerifyLicenseResponse(bool IsValid, string? UnlockToken);
public sealed record PublicConfiguration(bool PaywallEnabled, int FreeDumpLimit, string PriceDisplay, string? PayhipProductUrl, bool AiAvailable, string ProviderName, string ProviderPrivacyUrl);
public static class DumpValidation
{
    public static bool IsValid(ProcessDumpRequest r) => r.SourceDumpId != Guid.Empty && !string.IsNullOrWhiteSpace(r.Text) && r.Text.Trim().Length <= 5000 && r.Language is "nl-NL" or "en-US" && r.InputMethod is "text" or "speech" && r.SuccessfulDumpCount >= 0 && r.AvailableCategories is { Count: <= 25 } && r.AvailableCategories.All(c => c is not null && c.Id != Guid.Empty && c.Key is { Length: > 0 and <= 50 } && !string.IsNullOrWhiteSpace(c.DisplayName) && c.DisplayName.Length <= 30) && r.AvailableCategories.Select(c => c.Id).Distinct().Count() == r.AvailableCategories.Count;
}
