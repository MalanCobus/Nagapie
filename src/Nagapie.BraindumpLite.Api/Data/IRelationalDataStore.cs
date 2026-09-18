using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Api.Data;

public interface IRelationalDataStore
{
    Task<UserDocumentResponse> ReadAsync(string userId, string key, CancellationToken cancellationToken);
    Task<UserDocumentResponse> SaveThoughtsAsync(string userId, SaveThoughtsRequest request, CancellationToken cancellationToken);
    Task<UserDocumentResponse> SaveCategoriesAsync(string userId, SaveCategoriesRequest request, CancellationToken cancellationToken);
    Task<Guid?> SaveDraftAsync(string userId, SaveUserDocumentRequest? request, Guid version, CancellationToken cancellationToken);
    Task ClearAsync(string userId, CancellationToken cancellationToken);
    Task<List<SavedDumpResponse>> HistoryAsync(string userId, int page, CancellationToken cancellationToken);
}
