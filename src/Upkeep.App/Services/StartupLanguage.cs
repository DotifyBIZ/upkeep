using System.Globalization;
using Upkeep.App.Core.Settings;

namespace Upkeep.App.Services;

/// <summary>
/// Decides which language the app runs in, before any window exists.
/// <para>
/// This has to happen in <see cref="Program"/>, ahead of <c>Application.Start</c>: XAML resolves
/// every <c>x:Uid</c> against the resource context that exists when a page is first loaded, so a
/// language chosen later would apply to some screens and not others. That's also why Settings says
/// the choice takes effect after a restart.
/// </para>
/// </summary>
public static class StartupLanguage
{
    private static string? _resolved;

    /// <summary>
    /// Reads the saved override, if any, and otherwise follows Windows' display language. Called
    /// once at startup; the result is what <see cref="LocalizationService"/> loads.
    /// </summary>
    public static string Resolve()
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        // Synchronous on purpose: this runs before the dispatcher exists, so there is no UI thread
        // to block, and every later read of settings goes through the async service.
        var settings = FileAppSettingsService.LoadForStartup();
        string tag = string.IsNullOrEmpty(settings.LanguageOverride)
            ? CultureInfo.CurrentUICulture.Name
            : settings.LanguageOverride;

        // "pl", "pl-PL" and "pl-PL-x-whatever" all mean Polish here.
        _resolved = LocalizationService.SupportedLanguages.FirstOrDefault(
            supported => tag.StartsWith(supported[..2], StringComparison.OrdinalIgnoreCase))
            ?? LocalizationService.DefaultLanguage;

        return _resolved;
    }

    /// <summary>Applies the resolved language to this process, so both XAML resources and
    /// runtime-formatted strings agree on it.</summary>
    public static void Apply()
    {
        string tag = Resolve();
        var culture = new CultureInfo(tag);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
