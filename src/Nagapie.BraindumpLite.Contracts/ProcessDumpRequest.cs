namespace Nagapie.BraindumpLite.Contracts;

public sealed record ProcessDumpRequest(Guid SourceDumpId, string Text, string Language, string InputMethod, List<CategoryReferenceDto> AvailableCategories, string? UnlockToken, int SuccessfulDumpCount = 0);
