namespace Nagapie.BraindumpLite.Contracts;

public static class DumpValidation
{
    public static bool IsValid(ProcessDumpRequest request)
    {
        if (request.SourceDumpId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Text) ||
            request.Text.Trim().Length > 5000 ||
            request.Language is not ("nl-NL" or "en-US") ||
            request.InputMethod is not ("text" or "speech") ||
            request.SuccessfulDumpCount < 0 ||
            request.AvailableCategories is not { Count: <= 25 })
        {
            return false;
        }

        var categories = request.AvailableCategories;
        return categories.All(IsValidCategory) && categories.Select(category => category.Id).Distinct().Count() == categories.Count;
    }

    private static bool IsValidCategory(CategoryReferenceDto category) => category is not null &&
            category.Id != Guid.Empty &&
            category.Key is { Length: > 0 and <= 50 } &&
            !string.IsNullOrWhiteSpace(category.DisplayName) &&
            category.DisplayName.Length <= 30;
}
