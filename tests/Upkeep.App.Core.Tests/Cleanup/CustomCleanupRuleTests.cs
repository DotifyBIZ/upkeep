using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Cleanup;

public class CustomCleanupRuleTests : IDisposable
{
    private readonly FakeWellKnownPaths _paths = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _paths.Dispose();
    }

    [Fact]
    public void TryParse_FolderAndPattern_SplitsThem()
    {
        string folder = _paths.CreateUnder(Path.Combine("Users", "tester", "Renders"));

        Assert.True(CustomCleanupRule.TryParse(Path.Combine(folder, "*.cache"), _paths, out var rule, out var problem));

        Assert.Equal(CustomRuleProblem.None, problem);
        Assert.Equal(folder, rule.Root);
        Assert.Equal("*.cache", rule.Pattern);
    }

    [Fact]
    public void TryParse_FolderOnly_MatchesEverythingInIt()
    {
        string folder = _paths.CreateUnder(Path.Combine("Users", "tester", "Renders"));

        Assert.True(CustomCleanupRule.TryParse(folder, _paths, out var rule, out _));

        Assert.Equal("*", rule.Pattern);
    }

    [Fact]
    public void TryParse_BarePattern_MeansTheUsersOwnProfile()
    {
        // "*.bak" with no folder is the one case Upkeep guesses at, and it guesses the safest place.
        Assert.True(CustomCleanupRule.TryParse("*.bak", _paths, out var rule, out _));

        Assert.Equal(_paths.UserProfile, rule.Root);
        Assert.Equal("*.bak", rule.Pattern);
    }

    [Fact]
    public void TryParse_SurroundingQuotesAndSpaces_AreIgnored()
    {
        // Copying a path out of Explorer brings the quotes with it.
        string folder = _paths.CreateUnder(Path.Combine("Users", "tester", "Renders"));

        Assert.True(CustomCleanupRule.TryParse($"  \"{folder}\"  ", _paths, out var rule, out _));

        Assert.Equal(folder, rule.Root);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_NothingTyped_IsRefused(string? input)
    {
        Assert.False(CustomCleanupRule.TryParse(input, _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.Empty, problem);
    }

    [Fact]
    public void TryParse_WildcardInTheFolderPart_IsRefused()
    {
        // The point of the preview is that the scope is visible before it runs; "C:\*\cache" isn't.
        Assert.False(CustomCleanupRule.TryParse(@"C:\Users\*\AppData\*.log", _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.WildcardInFolder, problem);
    }

    [Fact]
    public void TryParse_RelativePath_IsRefused()
    {
        Assert.False(CustomCleanupRule.TryParse(@"Renders\*.cache", _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.NotAbsolute, problem);
    }

    [Fact]
    public void TryParse_BareWordWithNoWildcard_IsRefusedRatherThanGuessedAt()
    {
        Assert.False(CustomCleanupRule.TryParse("renders", _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.NotAbsolute, problem);
    }

    [Fact]
    public void TryParse_FolderThatIsNotThere_IsRefused()
    {
        string missing = Path.Combine(_paths.UserProfile, "NoSuchFolder");

        Assert.False(CustomCleanupRule.TryParse(Path.Combine(missing, "*.tmp"), _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.FolderMissing, problem);
    }

    [Fact]
    public void TryParse_WindowsItself_IsRefused()
    {
        // A wildcard loose in Windows is how a maintenance tool breaks a machine it cannot see, and
        // quarantine does not really undo that — the machine is already broken by then.
        Assert.False(CustomCleanupRule.TryParse(Path.Combine(_paths.WindowsDirectory, "*.log"), _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.OutsideAllowedArea, problem);
    }

    [Fact]
    public void TryParse_AnotherPersonsProfile_IsRefused()
    {
        string other = _paths.CreateUnder(Path.Combine("Users", "someone-else"));

        Assert.False(CustomCleanupRule.TryParse(Path.Combine(other, "*.bak"), _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.OutsideAllowedArea, problem);
    }

    [Fact]
    public void TryParse_UpkeepsOwnFolder_IsRefused()
    {
        // The quarantine and the session journals live here; a rule matching inside it would
        // quarantine the record of what it quarantined.
        string upkeep = _paths.CreateUnder(Path.Combine("LocalAppData", "Upkeep", "Quarantine"));

        Assert.False(CustomCleanupRule.TryParse(Path.Combine(upkeep, "*"), _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.OutsideAllowedArea, problem);
    }

    [Fact]
    public void TryParse_TheUsersOwnProfile_IsAllowed()
    {
        string documents = _paths.CreateUnder(Path.Combine("Users", "tester", "Documents"));

        Assert.True(CustomCleanupRule.TryParse(Path.Combine(documents, "*.bak"), _paths, out _, out _));
    }

    [Fact]
    public void TryParse_SystemDriveIsARealDriveRoot_StillRefusesWindows()
    {
        // The regression that only showed up against a real machine: a drive root already ends in a
        // separator, so the "is this on the system drive" check was comparing against "C:\\" and
        // every path on C: read as being somewhere else entirely.
        _paths.SystemDriveRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.False(CustomCleanupRule.TryParse(Path.Combine(windows, "*.dll"), _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.OutsideAllowedArea, problem);
    }

    [Fact]
    public void TryParse_AWholeDriveWithNoFilter_IsRefused()
    {
        // Every file on a drive is not a cleanup rule, whatever the preview would say about it.
        string driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.False(CustomCleanupRule.TryParse(driveRoot, _paths, out _, out var problem));
        Assert.Equal(CustomRuleProblem.TooBroad, problem);
    }

    [Fact]
    public void ParseAll_DropsWhatNoLongerParsesAndDeduplicates()
    {
        // The settings file is user-writable, and a folder saved last month may since have gone.
        string folder = _paths.CreateUnder(Path.Combine("Users", "tester", "Renders"));
        string rule = Path.Combine(folder, "*.cache");

        var parsed = CustomCleanupRule.ParseAll(
            [rule, rule, Path.Combine(_paths.WindowsDirectory, "*.dll"), "   "],
            _paths);

        Assert.Equal(folder, Assert.Single(parsed).Root);
    }
}
