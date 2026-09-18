using Nagapie.BraindumpLite.Contracts;

namespace Nagapie.BraindumpLite.Client.Domain;

public static class CategoryRules
{
    public static List<Category> CreateDefaults() => new[]
    {
        ("do", "sage"),
        ("plan", "blue"),
        ("idea", "yellow"),
        ("remember", "terra"),
        ("let-go", "stone")
    }.Select((category, index) => new Category(new Guid($"00000000-0000-0000-0000-{index + 1:000000000000}"), category.Item1, null, category.Item2, true)).ToList();
    public static List<Category> Save(IReadOnlyCollection<Category> categories, Category category)
    {
        var name = category.CustomName?.Trim();
        if (category.IsDefault || string.IsNullOrWhiteSpace(name) || name.Length > 30)
        {
            throw new InvalidDataException(ErrorCodes.CategoryInvalid);
        }

        var duplicateName = categories.Any(existing => existing.Id != category.Id && string.Equals(existing.CustomName, name, StringComparison.OrdinalIgnoreCase));
        if (duplicateName)
        {
            throw new InvalidDataException(ErrorCodes.CategoryInvalid);
        }

        var updated = categories.Where(existing => existing.Id != category.Id).Append(category with
        {
            CustomName = name
        }).ToList();
        if (updated.Count > 25)
        {
            throw new InvalidDataException(ErrorCodes.CategoryInvalid);
        }

        return updated;
    }
}
