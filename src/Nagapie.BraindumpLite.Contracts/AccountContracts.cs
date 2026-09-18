using System.Text.Json;

namespace Nagapie.BraindumpLite.Contracts;

public sealed record CredentialsRequest(string Email, string Password);
public sealed record AccountSession(string? UserId, string? Email, string RequestToken);
public sealed record UserDocumentResponse(JsonElement? Data, Guid Version, Dictionary<Guid, Guid>? RowVersions = null);
public sealed record SaveUserDocumentRequest(JsonElement Data, Guid Version);

public sealed record RowChange<T>(T Value, Guid Version);
public sealed record RowDelete(Guid Id, Guid Version);
public sealed record SaveThoughtsRequest(Guid Epoch, List<RowChange<Domain.BrainDumpItem>> Upserts,
    List<RowDelete> Deletes, Guid? CommitDumpId = null, Guid DraftVersion = default, Guid OperationId = default);
public sealed record SaveCategoriesRequest(Guid Epoch, List<RowChange<Domain.Category>> Upserts, List<RowDelete> Deletes);

public sealed record ThoughtQuery(int Page = 0, int PageSize = 50, Guid? CategoryId = null,
    bool Unsorted = false, bool? Completed = null, string? Horizon = null);
public sealed record ThoughtPage(List<Domain.BrainDumpItem> Items, Guid Epoch,
    Dictionary<Guid, Guid> RowVersions, bool HasMore, int SuccessfulAiDumps, bool DraftCommitted);
