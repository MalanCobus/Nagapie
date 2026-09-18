using Nagapie.BraindumpLite.Client.Domain;

namespace Nagapie.BraindumpLite.Client.Pages;

public partial class Settings
{
    private bool clear;
    private string newName = "", color = "sage", renameValue = "";
    private Category? deleteCategory, renaming;
    private Task LanguageAsync(string language) => Run(() => State.SaveSettingsAsync(State.Settings with { Language = language }));
    private Task RevokeAsync() => Run(() => State.SaveSettingsAsync(State.Settings with { HasConsentedToAiProcessing = false }));
    private Task AddAsync() => Run(async () =>
    {
        await State.SaveCategoryAsync(new(Guid.NewGuid(), "custom", newName, color, false));
        newName = "";
    });
    private void Rename(Category c)
    {
        renaming = c;
        renameValue = c.CustomName!;
    }

    private Task SaveRenameAsync() => Run(async () =>
    {
        await State.SaveCategoryAsync(renaming! with
        {
            CustomName = renameValue
        });
        renaming = null;
    });
    private Task DeleteCategoryAsync() => Run(async () =>
    {
        await State.DeleteCategoryAsync(deleteCategory!.Id);
        deleteCategory = null;
    });
    private Task ClearAsync() => Run(async () =>
    {
        await State.ClearAsync();
        clear = false;
        Nav.NavigateTo("/welcome");
    });
}
