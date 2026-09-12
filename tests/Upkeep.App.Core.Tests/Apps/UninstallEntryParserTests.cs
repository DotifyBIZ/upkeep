using Upkeep.App.Core.Apps;

namespace Upkeep.App.Core.Tests.Apps;

public class UninstallEntryParserTests
{
    private static UninstallEntry Entry(
        string? displayName = "Zoom Workplace",
        string? uninstallString = @"C:\Program Files\Zoom\uninstall.exe",
        int? systemComponent = null,
        string? parentKeyName = null,
        string? releaseType = null,
        string? quietUninstallString = null) =>
        new()
        {
            KeyName = "Zoom",
            DisplayName = displayName,
            UninstallString = uninstallString,
            SystemComponent = systemComponent,
            ParentKeyName = parentKeyName,
            ReleaseType = releaseType,
            QuietUninstallString = quietUninstallString,
        };

    [Fact]
    public void IsUserVisibleApp_OrdinaryApp_IsListed() =>
        Assert.True(UninstallEntryParser.IsUserVisibleApp(Entry()));

    [Fact]
    public void IsUserVisibleApp_NoDisplayName_IsNotListed() =>
        Assert.False(UninstallEntryParser.IsUserVisibleApp(Entry(displayName: null)));

    [Fact]
    public void IsUserVisibleApp_SystemComponent_IsNotListed()
    {
        // Windows hides these from Programs and Features, and so does Upkeep.
        Assert.False(UninstallEntryParser.IsUserVisibleApp(Entry(systemComponent: 1)));
    }

    [Fact]
    public void IsUserVisibleApp_EntryBelongingToAnotherProduct_IsNotListed()
    {
        // An entry with a parent is an update to that product, not an app of its own.
        Assert.False(UninstallEntryParser.IsUserVisibleApp(Entry(parentKeyName: "Office16")));
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Hotfix")]
    [InlineData("Security Update")]
    [InlineData("ServicePack")]
    public void IsUserVisibleApp_UpdatesAndPatches_AreNotListed(string releaseType)
    {
        // Listing these invites someone to uninstall a Windows update by mistake.
        Assert.False(UninstallEntryParser.IsUserVisibleApp(Entry(releaseType: releaseType)));
    }

    [Fact]
    public void IsUserVisibleApp_NothingToRun_IsNotListed()
    {
        // A row whose only button does nothing is worse than no row.
        Assert.False(UninstallEntryParser.IsUserVisibleApp(Entry(uninstallString: null)));
    }

    [Fact]
    public void IsUserVisibleApp_QuietUninstallStringOnly_IsStillListed() =>
        Assert.True(UninstallEntryParser.IsUserVisibleApp(Entry(uninstallString: null, quietUninstallString: @"C:\App\uninstall.exe /S")));

    [Fact]
    public void ParseCommand_QuotedExecutableWithArguments_SplitsCorrectly()
    {
        var command = UninstallEntryParser.ParseCommand(@"""C:\Program Files\Zoom\Installer.exe"" --uninstall --silent");

        Assert.NotNull(command);
        Assert.Equal(@"C:\Program Files\Zoom\Installer.exe", command.FileName);
        Assert.Equal(["--uninstall", "--silent"], command.Arguments);
    }

    [Fact]
    public void ParseCommand_UnquotedExecutable_TakesTheFirstToken()
    {
        var command = UninstallEntryParser.ParseCommand(@"C:\Windows\System32\msiexec.exe /X{12345678-1234-1234-1234-123456789012}");

        Assert.NotNull(command);
        Assert.EndsWith("msiexec.exe", command.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseCommand_MsiInstallSwitch_IsRewrittenToUninstall()
    {
        // The registry stores /I for Windows Installer products; running that opens a repair
        // dialog instead of removing anything.
        var command = UninstallEntryParser.ParseCommand(@"MsiExec.exe /I{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}");

        Assert.NotNull(command);
        Assert.Equal(["/X{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"], command.Arguments);
    }

    [Theory]
    [InlineData("/i{GUID}")]
    [InlineData("-I{GUID}")]
    [InlineData("-i{GUID}")]
    public void ParseCommand_EveryMsiInstallSpelling_IsRewritten(string argument)
    {
        var command = UninstallEntryParser.ParseCommand($"MsiExec.exe {argument}");

        Assert.NotNull(command);
        Assert.Equal("/X{GUID}", command.Arguments[0]);
    }

    [Fact]
    public void ParseCommand_NonMsiUninstaller_KeepsItsArgumentsAsTheyAre()
    {
        var command = UninstallEntryParser.ParseCommand(@"C:\App\unins000.exe /Install /Silent");

        Assert.NotNull(command);
        Assert.Equal(["/Install", "/Silent"], command.Arguments);
    }

    [Fact]
    public void ParseCommand_ArgumentContainingASpaceInQuotes_StaysOneArgument()
    {
        var command = UninstallEntryParser.ParseCommand(@"""C:\App\uninstall.exe"" --path ""C:\Program Files\App""");

        Assert.NotNull(command);
        Assert.Equal(["--path", @"C:\Program Files\App"], command.Arguments);
    }

    [Fact]
    public void ParseCommand_ExecutableOnly_HasNoArguments()
    {
        var command = UninstallEntryParser.ParseCommand(@"C:\App\unins000.exe");

        Assert.NotNull(command);
        Assert.Empty(command.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseCommand_NothingToParse_ReturnsNull(string? uninstallString) =>
        Assert.Null(UninstallEntryParser.ParseCommand(uninstallString));

    [Fact]
    public void ToBytes_ConvertsTheRegistrysKilobytes()
    {
        Assert.Equal(2048, UninstallEntryParser.ToBytes(2));
        Assert.Null(UninstallEntryParser.ToBytes(0));
        Assert.Null(UninstallEntryParser.ToBytes(null));
    }

    [Fact]
    public void ParseInstallDate_RegistryFormat_IsUnderstood() =>
        Assert.Equal(new DateOnly(2026, 3, 11), UninstallEntryParser.ParseInstallDate("20260311"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("11/03/2026")]
    [InlineData("not-a-date")]
    public void ParseInstallDate_AnythingElse_IsIgnored(string? installDate)
    {
        // Plenty of installers write whatever they like here; a bad date is not worth a failed scan.
        Assert.Null(UninstallEntryParser.ParseInstallDate(installDate));
    }
}
