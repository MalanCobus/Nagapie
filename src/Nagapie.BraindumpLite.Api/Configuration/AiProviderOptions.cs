namespace Nagapie.BraindumpLite.Api;

public sealed class AiProviderOptions
{
    public const string SectionName = "AiProvider";
    public string? ApiKey
    {
        get; set;
    }
    public string? Model
    {
        get; set;
    }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string DisplayName { get; set; } = "OpenAI";
    public string PrivacyUrl { get; set; } = "https://openai.com/policies/privacy-policy/";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}
