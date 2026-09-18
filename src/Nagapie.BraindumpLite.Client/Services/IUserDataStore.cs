namespace Nagapie.BraindumpLite.Client.Services;

public interface IUserDataStore
{
    Task<T?> ReadAsync<T>(string key);
    Task WriteAsync<T>(string key, T value);
    Task RemoveAsync(string key);
    async Task ClearAsync()
    {
        foreach (var key in new[] { "items", "categories", "draft", "settings", "access" })
        {
            await RemoveAsync(key);
        }
    }
}
