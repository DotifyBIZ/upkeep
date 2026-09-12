using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Upkeep.App.Core.Abstractions;

namespace Upkeep.App.Services;

/// <summary>
/// Resolves strings the app builds at runtime (a message with a count in it, a key returned by a
/// scanner) from the same <c>.resw</c> files that back XAML's <c>x:Uid</c>.
/// <para>
/// The files are embedded and parsed as XML here rather than read through a WinRT resource API:
/// every such API needs package identity, which this unpackaged app deliberately doesn't have
/// (see CLAUDE.md). Parsing the same source keeps one set of translations rather than a second
/// hand-maintained table in C#.
/// </para>
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultLanguage = "en-US";

    /// <summary>The languages Upkeep ships. Settings offers exactly these, plus "follow Windows".</summary>
    public static readonly IReadOnlyList<string> SupportedLanguages = ["en-US", "pl-PL"];

    private readonly FrozenDictionary<string, string> _strings;
    private readonly FrozenDictionary<string, string> _fallbackStrings;

    public LocalizationService()
        : this(StartupLanguage.Resolve())
    {
    }

    public LocalizationService(string languageTag)
    {
        CurrentLanguage = SupportedLanguages.Contains(languageTag) ? languageTag : DefaultLanguage;
        _strings = Load(CurrentLanguage);
        _fallbackStrings = CurrentLanguage == DefaultLanguage ? _strings : Load(DefaultLanguage);
    }

    public string CurrentLanguage { get; }

    public string GetString(string key)
    {
        if (_strings.TryGetValue(key, out string? value))
        {
            return value;
        }

        // A key present in one language but missing from another (a forgotten translation) falls
        // back to English rather than leaking a raw key into the UI.
        return _fallbackStrings.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public string GetString(string key, params object[] arguments)
    {
        string format = GetString(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, arguments);
        }
        catch (FormatException)
        {
            // A malformed placeholder in a translation shouldn't take down the page that renders
            // it; show the unformatted string instead.
            return format;
        }
    }

    private static FrozenDictionary<string, string> Load(string languageTag)
    {
        string resourceName = $"Upkeep.App.Strings.{languageTag}.Resources.resw";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return FrozenDictionary<string, string>.Empty;
        }

        var document = XDocument.Load(stream);
        return document.Root?
            .Elements("data")
            .Where(element => element.Attribute("name") is not null)
            .ToFrozenDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal)
            ?? FrozenDictionary<string, string>.Empty;
    }
}
