namespace Nagapie.BraindumpLite.Client.Pages;

public partial class Welcome
{
    private bool accepted;
    private Task LanguageAsync(string language) => Run(() => State.SaveSettingsAsync(State.Settings with { Language = language }));
    private Task StartAsync() => Run(async () =>
    {
        await State.SaveSettingsAsync(State.Settings with
        {
            HasCompletedOnboarding = true
        });
        Nav.NavigateTo("/");
    });
}
