using Windows.ApplicationModel.Resources;

namespace Optimizer.WinUI.Helpers;

/// <summary>
/// Thin wrapper around WinUI ResourceLoader for strongly-typed string lookups.
/// Keys in .resw files use the WinUI x:Uid naming convention, e.g. a TextBlock
/// with x:Uid="Dashboard_Title" gets its Text from the key "Dashboard_Title.Text".
/// </summary>
public static class Localization
{
    private static ResourceLoader? _loader;

    private static ResourceLoader Loader => _loader ??= new ResourceLoader();

    /// <summary>Returns the localized string for the given resource key.</summary>
    public static string Get(string key)
    {
        try
        {
            return Loader.GetString(key) ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// Changes the application language. The change takes effect on the next app launch.
    /// Call this when the user selects a language in Settings, then prompt for restart.
    /// </summary>
    public static void SetLanguage(string languageCode)
    {
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode;
            // Reset the cached loader so the next Get() picks up the new language
            _loader = null;
        }
        catch { /* non-fatal */ }
    }
}
