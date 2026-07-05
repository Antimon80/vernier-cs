using System.Globalization;
using App.Resources.Strings;

namespace App.Services;

public sealed class LocalizationService
{
    public event EventHandler? LanguageChanged;

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    public void SetLanguage(string cultureName)
    {
        CultureInfo culture = new(cultureName);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        AppResources.Culture = culture;

        CurrentCulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}