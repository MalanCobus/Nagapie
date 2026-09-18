using System.Collections;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using Nagapie.BraindumpLite.Client.Resources;

namespace Nagapie.BraindumpLite.Client.Services;
// Embed both languages for immediate offline language switching.
public sealed class AppLocalizer(AppState state) : IStringLocalizer<AppResources>
{
    private static readonly ResourceManager English = new("Nagapie.BraindumpLite.Client.Resources.AppResources", typeof(AppResources).Assembly);
    private static readonly ResourceManager Dutch = new("Nagapie.BraindumpLite.Client.Resources.AppResources.nl", typeof(AppResources).Assembly);
    private ResourceManager Current => state.Settings.Language == "nl-NL" ? Dutch : English;

    public LocalizedString this[string name] => new(name, Current.GetString(name, CultureInfo.InvariantCulture) ?? name, Current.GetString(name, CultureInfo.InvariantCulture) is null);
    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(CultureInfo.GetCultureInfo(state.Settings.Language), this[name].Value, arguments), this[name].ResourceNotFound);
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Current.GetResourceSet(CultureInfo.InvariantCulture, true, true)!.Cast<DictionaryEntry>().Select(e => new LocalizedString((string)e.Key, (string)e.Value!));
}
