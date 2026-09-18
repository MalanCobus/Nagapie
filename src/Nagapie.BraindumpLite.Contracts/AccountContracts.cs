using System.Text.Json;

namespace Nagapie.BraindumpLite.Contracts;

public sealed record CredentialsRequest(string Email, string Password);
public sealed record AccountSession(string? UserId, string? Email, string RequestToken);
public sealed record UserDocumentResponse(JsonElement? Data, Guid Version, Dictionary<Guid, Guid>? RowVersions = null);
public sealed record SaveUserDocumentRequest(JsonElement Data, Guid Version);

public sealed record RowChange<T>(T Value, Guid Version);
public sealed record RowDelete(Guid Id, Guid Version);
public sealed record SaveThoughtsRequest(Guid Epoch, List<RowChange<Domain.BrainDumpItem>> Upserts,
    List<RowDelete> Deletes, Guid? CommitDumpId = null, Guid DraftVersion = default);
public sealed record SaveCategoriesRequest(Guid Epoch, List<RowChange<Domain.Category>> Upserts, List<RowDelete> Deletes);
