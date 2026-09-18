using System.Text.Json;
using Nagapie.BraindumpLite.Contracts;
namespace Nagapie.BraindumpLite.Api.Data;

public interface IUserDocumentStore
{
    Task<UserDocumentResponse> ReadAsync(string userId, string key, CancellationToken cancellationToken);
    Task<Guid?> SaveAsync(string userId, string key, JsonElement? data, Guid version, CancellationToken cancellationToken);
}

