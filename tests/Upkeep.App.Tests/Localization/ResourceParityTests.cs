using System.Reflection;
using System.Xml.Linq;
using Upkeep.App.Services;

namespace Upkeep.App.Tests.Localization;

/// <summary>
/// Upkeep ships English and Polish as equals (README, CLAUDE.md), which only holds if the two
/// resource files actually carry the same keys. A forgotten translation shows up here rather than
/// as an English string on a Polish screen.
/// </summary>
public class ResourceParityTests
{
    private static readonly Assembly AppAssembly = typeof(LocalizationService).Assembly;

    private static Dictionary<string, string> ReadResources(string languageTag)
    {
        string resourceName = $"Upkeep.App.Strings.{languageTag}.Resources.resw";
        using var stream = AppAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"{resourceName} is not embedded in the app assembly.");

        return XDocument.Load(stream).Root!
            .Elements("data")
            .Where(element => element.Attribute("name") is not null)
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    [Fact]
    public void EveryLanguageShipsTheSameKeys()
    {
        var english = ReadResources("en-US");
        var polish = ReadResources("pl-PL");

        string[] missingFromPolish = [.. english.Keys.Except(polish.Keys).Order()];
        string[] missingFromEnglish = [.. polish.Keys.Except(english.Keys).Order()];

        Assert.Empty(missingFromPolish);
        Assert.Empty(missingFromEnglish);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl-PL")]
    public void NoResourceIsEmpty(string languageTag)
    {
        var resources = ReadResources(languageTag);

        string[] empty = [.. resources.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).Order()];

        Assert.Empty(empty);
    }

    [Fact]
    public void FormatPlaceholdersMatchBetweenLanguages()
    {
        // A translation that drops a {0}, or invents a {1}, throws at the point it is formatted —
        // which is at runtime, on the screen that uses it.
        var english = ReadResources("en-US");
        var polish = ReadResources("pl-PL");

        foreach ((string key, string englishValue) in english)
        {
            if (!polish.TryGetValue(key, out string? polishValue))
            {
                continue;
            }

            Assert.Equal(PlaceholderCount(englishValue), PlaceholderCount(polishValue));
        }
    }

    private static int PlaceholderCount(string value)
    {
        int count = 0;
        for (int index = 0; index < 10; index++)
        {
            if (value.Contains($"{{{index}}}", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void EveryShippedLanguageHasAResourceFile()
    {
        foreach (string language in LocalizationService.SupportedLanguages)
        {
            Assert.NotEmpty(ReadResources(language));
        }
    }
}
