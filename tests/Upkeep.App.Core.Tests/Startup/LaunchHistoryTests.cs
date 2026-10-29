using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Startup;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Startup;

public class LaunchHistoryTests
{
    private static readonly DateTimeOffset LastWeek = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>UserAssist's 72-byte record with only the last-run time filled in.</summary>
    private static byte[] Record(DateTimeOffset lastRun)
    {
        byte[] data = new byte[72];
        BitConverter.GetBytes(lastRun.UtcDateTime.ToFileTimeUtc()).CopyTo(data, 60);
        return data;
    }

    [Theory]
    [InlineData("Uryyb, Jbeyq!", "Hello, World!")]
    [InlineData(@"{7P5N40RS-N0SO}\JvaFPC\JvaFPC.rkr", @"{7C5A40EF-A0FB}\WinSCP\WinSCP.exe")]
    public void Rot13_DecodesValueNames(string encoded, string decoded)
    {
        Assert.Equal(decoded, UserAssistParser.Rot13(encoded));
        Assert.Equal(encoded, UserAssistParser.Rot13(decoded));
    }

    [Fact]
    public void LastRun_ReadsTheFileTimeAtOffsetSixty()
    {
        Assert.Equal(LastWeek, UserAssistParser.LastRun(Record(LastWeek)));
    }

    [Fact]
    public void LastRun_ShortEmptyOrZero_IsNull()
    {
        Assert.Null(UserAssistParser.LastRun(null));
        Assert.Null(UserAssistParser.LastRun(new byte[16]));
        Assert.Null(UserAssistParser.LastRun(new byte[72]));

        byte[] negative = new byte[72];
        BitConverter.GetBytes(-1L).CopyTo(negative, 60);
        Assert.Null(UserAssistParser.LastRun(negative));

        byte[] tooLate = new byte[72];
        BitConverter.GetBytes(long.MaxValue).CopyTo(tooLate, 60);
        Assert.Null(UserAssistParser.LastRun(tooLate));
    }

    [Fact]
    public void Tokens_SplitPathsAndAppIdsIntoWordsWorthMatching()
    {
        Assert.Equal(["steam"], LaunchHistoryMatch.Tokens(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Steam\steam.exe").Where(t => t == "steam"));
        Assert.Contains("steam", LaunchHistoryMatch.Tokens("Valve.Steam.Client"));
        Assert.Contains("msedge", LaunchHistoryMatch.Tokens("MSEdge"));
        Assert.DoesNotContain("microsoft", LaunchHistoryMatch.Tokens("Microsoft.VisualStudioCode"));
        Assert.DoesNotContain("exe", LaunchHistoryMatch.Tokens(@"C:\a\Discord.exe"));
    }

    [Theory]
    [InlineData("\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", "steam")]
    [InlineData("C:\\Program Files\\Logi\\LGHUB\\lghub.exe --background", "lghub")]
    [InlineData("C:\\Program Files\\Spotify\\Spotify.exe", "Spotify")]
    [InlineData("rundll32.exe C:\\x\\y.dll,Start", "rundll32")]
    [InlineData("C:\\tools\\agent --quiet", "agent")]
    [InlineData("C:\\Users\\me\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\Notes.lnk", "Notes")]
    [InlineData("\"C:\\unterminated\\app.exe", "app")]
    public void ExecutableStem_PicksTheProgramOutOfTheCommand(string command, string expected)
    {
        Assert.Equal(expected, StartupCommand.ExecutableStem(command));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("\"\"")]
    public void ExecutableStem_NothingToRead_IsNull(string? command)
    {
        Assert.Null(StartupCommand.ExecutableStem(command));
    }

    [Fact]
    public void LastStartedByUser_MatchesByWordAndKeepsTheLatest()
    {
        var registry = new FakeRegistryProbe();
        string exes = UserAssistLaunchHistory.CountKeys[0];
        string shortcuts = UserAssistLaunchHistory.CountKeys[1];

        registry.AddValue(RegistryHiveName.CurrentUser, exes, UserAssistParser.Rot13("Valve.Steam.Client"), Record(LastWeek.AddDays(-20)));
        registry.AddValue(RegistryHiveName.CurrentUser, shortcuts, UserAssistParser.Rot13(@"{A77F5D77}\Steam\Steam.lnk"), Record(LastWeek));
        registry.AddValue(RegistryHiveName.CurrentUser, exes, UserAssistParser.Rot13("Broken"), new byte[8]);

        var history = new UserAssistLaunchHistory(registry);

        Assert.Equal(LastWeek, history.LastStartedByUser(["Steam", "steam"]));
        Assert.Null(history.LastStartedByUser(["Broken"]));
        Assert.Null(history.LastStartedByUser(["Discord", ""]));
    }

    [Fact]
    public void KeysFor_IsTheItemNameAndWhatItRuns()
    {
        var item = new StartupItem { Id = "x", Name = "MicrosoftEdgeAutoLaunch_6F8A", Command = "\"C:\\Edge\\msedge.exe\" --no-startup-window", Source = StartupSource.RunKeyCurrentUser };

        Assert.Equal(["MicrosoftEdgeAutoLaunch_6F8A", "msedge"], LaunchHistoryMatch.KeysFor(item));
    }
}
