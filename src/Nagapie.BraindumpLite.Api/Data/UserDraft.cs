namespace Nagapie.BraindumpLite.Api.Data;

public sealed class UserDraft
{
    public string UserId { get; set; } = "";
    public Guid Id
    {
        get; set;
    }
    public string Text { get; set; } = "";
    public string InputMethod { get; set; } = "text";
    public bool WasAiProcessed
    {
        get; set;
    }
    // Temporary review suggestions are a document, not yet committed thoughts.
    public string? ReviewJson
    {
        get; set;
    }
    public Guid Version { get; set; } = Guid.NewGuid();
    public bool IsDeleted
    {
        get; set;
    }
}
