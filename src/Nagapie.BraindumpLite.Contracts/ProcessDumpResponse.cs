namespace Nagapie.BraindumpLite.Contracts;

public sealed record ProcessDumpResponse(Guid SourceDumpId, List<SplitItemDto> Items, string PromptVersion);
