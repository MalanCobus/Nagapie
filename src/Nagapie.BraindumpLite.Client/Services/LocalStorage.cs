using System.Text.Json;
using Microsoft.JSInterop;
using Nagapie.BraindumpLite.Client.Domain;
using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Services;

public sealed class LocalStorage(IJSRuntime js) : ILocalStorageService
{
    private const string Prefix = "nagapie.braindump.";
    public async Task<T?> ReadAsync<T>(string key)
    {
        var text = await js.InvokeAsync<string?>("nagapie.storageGet", Prefix + key);
        if (text is null)
        {
            return default;
        }

        var envelope = JsonSerializer.Deserialize<StorageEnvelope<T>>(text);
        if (envelope is null || envelope.SchemaVersion != 1 || envelope.Data is null)
        {
            throw new InvalidDataException(ErrorCodes.StorageInvalid);
        }

        return envelope.Data;
    }

    public async Task WriteAsync<T>(string key, T value) => await js.InvokeVoidAsync("nagapie.storageSet", Prefix + key, JsonSerializer.Serialize(new StorageEnvelope<T>(1, value)));
    public async Task RemoveAsync(string key) => await js.InvokeVoidAsync("nagapie.storageRemove", Prefix + key);
}
