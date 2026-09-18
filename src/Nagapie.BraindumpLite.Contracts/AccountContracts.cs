using System.Text.Json;

namespace Nagapie.BraindumpLite.Contracts;

public sealed record CredentialsRequest(string Email, string Password);
public sealed record AccountSession(string? UserId, string? Email, string RequestToken);
public sealed record UserDocumentResponse(JsonElement? Data, Guid Version);
public sealed record SaveUserDocumentRequest(JsonElement Data, Guid Version);

