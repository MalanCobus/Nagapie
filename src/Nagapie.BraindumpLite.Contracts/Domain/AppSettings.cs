namespace Nagapie.BraindumpLite.Contracts.Domain;

public sealed record AppSettings
{
    public string Language { get; set; } = "nl-NL";
    public bool HasCompletedOnboarding
    {
        get; set;
    }
    public bool HasConsentedToAiProcessing
    {
        get; set;
    }
    public bool HasAcknowledgedSpeech
    {
        get; set;
    }
}
