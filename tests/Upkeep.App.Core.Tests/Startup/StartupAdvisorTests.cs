using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.Core.Tests.Startup;

public class StartupAdvisorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeLaunchHistory : ILaunchHistory
    {
        public Dictionary<string, DateTimeOffset> Launches { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset? LastStartedByUser(IEnumerable<string> keys)
        {
            var hits = keys.Select(LaunchHistoryMatch.Normalize).Where(Launches.ContainsKey).Select(key => Launches[key]).ToList();
            return hits.Count == 0 ? null : hits.Max();
        }
    }

    private static StartupItem Item(string name, string? command, bool enabled = true, StartupSource source = StartupSource.RunKeyCurrentUser) =>
        new() { Id = name, Name = name, Command = command, Source = source, IsEnabled = enabled };

    private static SlowStartupApp Slow(string file, int totalMs) =>
        new(Now.AddDays(-1), file, null, TimeSpan.FromMilliseconds(totalMs), TimeSpan.FromMilliseconds(totalMs / 3));

    [Fact]
    public void Analyse_AveragesWhatWindowsMeasuredAndRanksSlowestFirst()
    {
        var items = new[]
        {
            Item("Alpha", @"C:\a\alpha.exe"),
            Item("Steam", "\"C:\\Steam\\steam.exe\" -silent"),
            Item("Spotify", @"C:\s\Spotify.exe"),
        };
        var performance = new BootPerformance([], [Slow("steam.exe", 2000), Slow("Steam.exe", 3000), Slow("Spotify.exe", 1000)]);

        var insights = StartupAdvisor.Analyse(items, performance, new FakeLaunchHistory(), Now);

        Assert.Equal(["Steam", "Spotify", "Alpha"], insights.Select(insight => insight.Item.Name));
        Assert.Equal(new StartupTiming(TimeSpan.FromMilliseconds(2500), 2), insights[0].Timing);
        Assert.Null(insights[2].Timing);
    }

    [Fact]
    public void Analyse_SuggestsOnlySlowAppsThePersonHasntStartedInThirtyDays()
    {
        var history = new FakeLaunchHistory();
        history.Launches["spotify"] = Now.AddDays(-2);
        history.Launches["discord"] = Now.AddDays(-45);

        var items = new[]
        {
            Item("Steam", @"C:\Steam\steam.exe"),
            Item("Spotify", @"C:\s\Spotify.exe"),
            Item("Discord", @"C:\d\Discord.exe"),
            Item("Untimed", @"C:\u\untimed.exe"),
        };
        var performance = new BootPerformance([], [Slow("steam.exe", 2000), Slow("Spotify.exe", 1500), Slow("Discord.exe", 1200)]);

        var suggested = StartupAdvisor.Analyse(items, performance, history, Now)
            .Where(insight => insight.Suggested)
            .Select(insight => insight.Item.Name);

        Assert.Equal(["Steam", "Discord"], suggested);
    }

    [Fact]
    public void Analyse_NeverSuggestsWhatIsOffUnswitchableOrWindowsSecurity()
    {
        var items = new[]
        {
            Item("Off", @"C:\o\off.exe", enabled: false),
            Item("Once", @"C:\r\once.exe", source: StartupSource.RunOnce),
            Item("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe"),
        };
        var performance = new BootPerformance([], [Slow("off.exe", 2000), Slow("once.exe", 2000), Slow("SecurityHealthSystray.exe", 2000)]);

        Assert.DoesNotContain(StartupAdvisor.Analyse(items, performance, new FakeLaunchHistory(), Now), insight => insight.Suggested);
    }

    [Fact]
    public void Analyse_NothingMeasured_SuggestsNothingAndKeepsOrder()
    {
        var items = new[] { Item("B", @"C:\b.exe"), Item("A", null) };

        var insights = StartupAdvisor.Analyse(items, null, new FakeLaunchHistory(), Now);

        Assert.Equal(["B", "A"], insights.Select(insight => insight.Item.Name));
        Assert.All(insights, insight => Assert.False(insight.Suggested));
    }

    [Fact]
    public void Analyse_SlowAppWithoutAName_IsIgnored()
    {
        var performance = new BootPerformance([], [Slow(".exe", 2000)]);

        var insight = Assert.Single(StartupAdvisor.Analyse([Item("A", @"C:\a.exe")], performance, new FakeLaunchHistory(), Now));

        Assert.Null(insight.Timing);
    }
}
