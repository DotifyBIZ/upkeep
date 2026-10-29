using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Tests.Performance;

public class SpeedCheckTests
{
    private static readonly DateTimeOffset Change = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    private static BootRecord Boot(double daysFromChange, double seconds) =>
        new(Change.AddDays(daysFromChange), TimeSpan.FromSeconds(seconds));

    private static SessionManifest Session(string id, params SessionEntry[] entries) => new()
    {
        Id = id,
        Kind = SessionKind.Startup,
        Entries = entries,
    };

    [Fact]
    public void Compare_AveragesEachSideOfTheChange()
    {
        BootRecord[] boots = [Boot(-3, 44), Boot(-2, 48), Boot(-1, 46), Boot(1, 35), Boot(2, 33)];

        var comparison = SpeedCheck.Compare(boots, Change);

        Assert.Equal(new SpeedComparison(TimeSpan.FromSeconds(46), 3, TimeSpan.FromSeconds(34), 2), comparison);
        Assert.Equal(TimeSpan.FromSeconds(12), comparison?.Saved);
        Assert.True(comparison?.IsSmallSample);
    }

    [Fact]
    public void Compare_UsesOnlyTheNearestTenEitherSide()
    {
        var before = Enumerable.Range(1, 15).Select(day => Boot(-day, day <= 10 ? 40 : 100));
        var after = Enumerable.Range(1, 4).Select(day => Boot(day, 30));

        var comparison = SpeedCheck.Compare(before.Concat(after), Change);

        Assert.Equal(TimeSpan.FromSeconds(40), comparison?.BeforeAverage);
        Assert.Equal(10, comparison?.BeforeCount);
        Assert.False(comparison?.IsSmallSample);
    }

    [Fact]
    public void Compare_NoStartSinceTheChange_HasNothingToSayYet()
    {
        Assert.Null(SpeedCheck.Compare([Boot(-1, 40)], Change));
        Assert.Null(SpeedCheck.Compare([Boot(1, 40)], Change));
    }

    [Fact]
    public void Compare_SlowerAfter_SaysSo()
    {
        Assert.Equal(TimeSpan.FromSeconds(-5), SpeedCheck.Compare([Boot(-1, 40), Boot(1, 45)], Change)?.Saved);
    }

    [Fact]
    public void Recent_IsTheNewestTenNewestFirst()
    {
        var recent = SpeedCheck.Recent(Enumerable.Range(0, 12).Select(day => Boot(day, 40)));

        Assert.Equal(10, recent.Count);
        Assert.Equal(Change.AddDays(11), recent[0].At);
        Assert.Null(SpeedCheck.Average([]));
    }

    [Fact]
    public void LastChange_IsTheNewestUnrevertedSessionWithASpeedChange()
    {
        SessionManifest[] sessions =
        [
            Session("old", new StartupItemToggledEntry("A", "RunKeyCurrentUser", true) { Completed = true, At = Change.AddDays(-10) }),
            Session("latest",
                new SystemSettingChangedEntry(PerformanceSettingIds.PowerModePluggedIn, "BestPowerEfficiency", "Balanced") { Completed = true, At = Change.AddMinutes(5) },
                new StartupItemToggledEntry("B", "RunKeyCurrentUser", true) { Completed = true, At = Change },
                new StartupItemToggledEntry("C", "RunKeyCurrentUser", true) { Completed = false, At = Change.AddMinutes(-30) }),
            Session("reverted", new BackgroundAccessChangedEntry("A_abcdefghjkmnp", "A", [], false) { Completed = true, At = Change.AddDays(1) })
                with { RevertedAt = Change.AddDays(2) },
            Session("cleanup", new FileDeletedEntry(@"C:\t.tmp", 1) { Completed = true, At = Change.AddDays(3) }),
            Session("drivers", new SystemSettingChangedEntry("windows-update-pause", "", "x") { Completed = true, At = Change.AddDays(3) }),
        ];

        Assert.Equal(new SpeedChange(Change, 2, "latest"), SpeedCheck.LastChange(sessions));
    }

    [Fact]
    public void LastChange_NothingChanged_IsNull()
    {
        Assert.Null(SpeedCheck.LastChange([Session("empty")]));
    }
}
