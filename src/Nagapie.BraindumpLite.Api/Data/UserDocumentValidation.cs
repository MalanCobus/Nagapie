using System.Text.Json;
using Nagapie.BraindumpLite.Contracts.Domain;

namespace Nagapie.BraindumpLite.Api.Data;

public static class UserDocumentValidation
{
    public static readonly string[] Keys = ["settings", "items", "categories", "draft", "access"];

    public static bool IsValid(string key, JsonElement data)
    {
        try
        {
            if (data.ValueKind != JsonValueKind.Object && key != "categories")
            {
                return false;
            }

            var settings = key == "settings" ? data.Deserialize<AppSettings>(JsonSerializerOptions.Web)! : new();
            var store = key == "items" ? data.Deserialize<ItemStore>(JsonSerializerOptions.Web)! : new();
            var categories = key == "categories" ? data.Deserialize<List<Category>>(JsonSerializerOptions.Web)! : CategoryRules.CreateDefaults();
            var draft = key == "draft" ? data.Deserialize<Draft>(JsonSerializerOptions.Web)! : new();
            if (key == "access")
            {
                return data.Deserialize<AccessState>(JsonSerializerOptions.Web)?.UnlockToken is null or { Length: <= 2048 };
            }

            StoredDataValidator.Validate(settings, store, categories, draft);
            return Keys.Contains(key) && store.Items.Count <= 5000 &&
                store.Items.Select(item => item.Id).Distinct().Count() == store.Items.Count &&
                store.SavedDumps.Count <= 10000 && store.AiDumps.Count <= 10000 &&
                (draft.Review?.Count ?? 0) <= 30;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NullReferenceException)
        {
            return false;
        }
    }
}

