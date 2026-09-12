namespace Upkeep.App.Core.Abstractions;

/// <summary>
/// Resolves a resource key to text in the current language. Core never holds user-facing strings:
/// a scanner returns a key (and format arguments), and the shell turns that into words. That's
/// what keeps the same operation usable from a UI, a log line and a test.
/// </summary>
public interface ILocalizationService
{
    /// <summary>The BCP-47 tag actually in use, after any Settings override.</summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// The string for <paramref name="key"/>, falling back to English and then to the key itself,
    /// so a missing translation degrades instead of throwing mid-render.
    /// </summary>
    string GetString(string key);

    /// <summary>Formats the string for <paramref name="key"/> with <paramref name="arguments"/>.</summary>
    string GetString(string key, params object[] arguments);
}
