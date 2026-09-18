using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Contracts.Domain;

public static class StoredDataValidator
{
    public static void Validate(AppSettings settings, ItemStore store, List<Category> categories, Draft draft)
    {
        var invalidSettings = settings.Language is not ("nl-NL" or "en-US");
        var invalidStore = store.Items is null || store.SavedDumps is null || store.AiDumps is null;
        var invalidDraft = draft.Id == Guid.Empty || draft.Text is null || draft.Text.Length > 5000 || draft.InputMethod is not ("text" or "speech");
        if (invalidSettings || invalidStore || invalidDraft || categories is null)
        {
            throw new InvalidDataException(ErrorCodes.StorageInvalid);
        }

        var invalidCategories = categories.Count > 25 || categories.Any(IsInvalidCategory) || categories.Select(category => category.Id).Distinct().Count() != categories.Count;
        var items = store.Items!;
        if (invalidCategories || items.Concat(draft.Review ?? []).Any(IsInvalidItem) ||
            items.Count > 5000 || items.Select(item => item.Id).Distinct().Count() != items.Count ||
            (draft.Review?.Count ?? 0) > 30)
        {
            throw new InvalidDataException(ErrorCodes.StorageInvalid);
        }
    }

    private static bool IsInvalidCategory(Category category) => category is null ||
            category.Id == Guid.Empty ||
            category.Key is null ||
            category.ColorToken is not ("sage" or "blue" or "yellow" or "terra" or "stone") ||
            (!category.IsDefault &&
            (string.IsNullOrWhiteSpace(category.CustomName) ||
            category.CustomName.Length > 30));
    private static bool IsInvalidItem(BrainDumpItem item) => item is null ||
            item.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(item.Text) ||
            item.Text.Length > 5000 ||
            item.InputMethod is not ("text" or "speech") ||
            !PlanningHorizons.IsValid(item) ||
            item.CompletionReason is not (null or "completed" or "let-go") ||
            (item.CompletionReason is null) != (item.CompletedAtUtc is null);
}
