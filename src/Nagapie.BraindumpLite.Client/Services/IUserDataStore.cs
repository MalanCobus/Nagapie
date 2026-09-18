using Nagapie.BraindumpLite.Contracts;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Client.Services;

public interface IUserDataStore
{
    Task<AppSettings> ReadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task<AccessState> ReadAccessAsync();
    Task SaveAccessAsync(AccessState access);
    Task<Draft> ReadDraftAsync();
    Task SaveDraftAsync(Draft draft);
    Task DeleteDraftAsync();
    Task<List<Category>> ReadCategoriesAsync();
    Task SaveCategoryAsync(Category category);
    Task DeleteCategoryAsync(Guid id);
    Task<ThoughtPage> QueryThoughtsAsync(ThoughtQuery query);
    Task<List<BrainDumpItem>> CommitDraftAsync(Draft draft);
    Task<BrainDumpItem> UpdateThoughtAsync(BrainDumpItem item);
    Task DeleteThoughtAsync(Guid id);
    Task ClearAsync();
}
