namespace Nagapie.BraindumpLite.Client.Services;

public interface ILocalStorageService
{
    Task<T?> ReadAsync<T>(string key);
    Task WriteAsync<T>(string key, T value);
    Task RemoveAsync(string key);
}
