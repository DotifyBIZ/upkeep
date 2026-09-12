using Upkeep.App.Services;

namespace Upkeep.App.Tests.Localization;

public class LocalizationServiceTests
{
    [Fact]
    public void GetString_KnownKey_ReturnsThatLanguagesText()
    {
        var english = new LocalizationService("en-US");
        var polish = new LocalizationService("pl-PL");

        Assert.Equal("Home", english.GetString("NavHome.Content"));
        Assert.Equal("Start", polish.GetString("NavHome.Content"));
    }

    [Fact]
    public void GetString_UnknownKey_ReturnsTheKeyRatherThanThrowing()
    {
        var localization = new LocalizationService("en-US");

        Assert.Equal("NoSuchKey", localization.GetString("NoSuchKey"));
    }

    [Fact]
    public void GetString_WithArguments_FormatsThePlaceholder()
    {
        var localization = new LocalizationService("en-US");

        Assert.Equal("Upkeep 1.3.0 is available.", localization.GetString("HomeUpdateAvailableFormat", "1.3.0"));
    }

    [Fact]
    public void GetString_WrongArgumentCount_ReturnsTheUnformattedStringInsteadOfThrowing()
    {
        // A bad translation must not take down the page rendering it.
        var localization = new LocalizationService("en-US");

        string result = localization.GetString("HomeUpdateAvailableFormat");

        Assert.Contains("{0}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_UnsupportedLanguage_FallsBackToEnglish()
    {
        var localization = new LocalizationService("de-DE");

        Assert.Equal(LocalizationService.DefaultLanguage, localization.CurrentLanguage);
        Assert.Equal("Home", localization.GetString("NavHome.Content"));
    }

    [Fact]
    public void SupportedLanguages_AreTheTwoUpkeepShips()
    {
        Assert.Equal(["en-US", "pl-PL"], LocalizationService.SupportedLanguages);
    }
}
