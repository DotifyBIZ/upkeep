using Upkeep.App.Core.Milestones;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Tests.Milestones;

public class MilestoneEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static SessionManifest Session(DateTimeOffset at, params SessionEntry[] entries) => new()
    {
        Id = SessionManifest.NewId(at),
        Kind = SessionKind.Cleanup,
        StartedAt = at,
        Entries = entries,
    };

    private static SessionManifest Cleaned(DateTimeOffset at, long bytes = 1024) =>
        Session(at, new FileDeletedEntry(@"C:\t.tmp", bytes) { Completed = true, At = at });

    private static MilestoneProgress Evaluate(IReadOnlyList<SessionManifest> sessions, BootPerformance? performance = null, int? startupAppsOn = null) =>
        MilestoneEvaluator.Evaluate(sessions, performance, startupAppsOn, Now, TimeZoneInfo.Utc);

    private static Milestone Get(MilestoneProgress progress, string id) => progress.Milestones.Single(milestone => milestone.Id == id);

    [Fact]
    public void Evaluate_NothingDoneYet_HasNothingAchieved()
    {
        var progress = Evaluate([]);

        Assert.Equal(0, progress.Rhythm.Streak);
        Assert.Equal(6, progress.Rhythm.Months.Count);
        Assert.True(progress.Rhythm.Months[^1].IsCurrent);
        Assert.All(progress.Milestones, milestone => Assert.False(milestone.Achieved));
        Assert.Null(Get(progress, MilestoneEvaluator.UnderFortySeconds).Progress);
        Assert.Null(Get(progress, MilestoneEvaluator.LeanStartup).Progress);
    }

    [Fact]
    public void Evaluate_CountsMonthsInARow_AndThisMonthDoesNotBreakTheRunYet()
    {
        SessionManifest[] sessions =
        [
            Cleaned(new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero)),
            Cleaned(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero)),
            Cleaned(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero)),
            Cleaned(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)),
            Cleaned(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)),
        ];

        var rhythm = Evaluate(sessions).Rhythm;

        Assert.Equal(3, rhythm.Streak);
        Assert.False(rhythm.DoneThisMonth);
        // May to October: June was missed, so the run is July to September.
        Assert.Equal([true, false, true, true, true, false], rhythm.Months.Select(month => month.Done));
    }

    [Fact]
    public void Evaluate_DoneThisMonth_CountsIt()
    {
        var progress = Evaluate([Cleaned(Now.AddDays(-1)), Cleaned(Now.AddMonths(-1))]);

        Assert.Equal(2, progress.Rhythm.Streak);
        Assert.True(progress.Rhythm.DoneThisMonth);
        Assert.True(Get(progress, MilestoneEvaluator.FirstTuneUp).Achieved);
        Assert.Equal(2.0 / 3, Get(progress, MilestoneEvaluator.ThreeMonths).Progress);
    }

    [Fact]
    public void Evaluate_RevertedOrEmptySessions_EarnNothing()
    {
        var reverted = Cleaned(Now.AddDays(-1), MilestoneEvaluator.TenGigabytesInBytes) with { RevertedAt = Now };
        var nothingCompleted = Session(Now.AddDays(-2), new FileDeletedEntry(@"C:\t.tmp", 5) { Completed = false });

        var progress = Evaluate([reverted, nothingCompleted]);

        Assert.Equal(0, progress.FreedBytes);
        Assert.Equal(0, progress.Rhythm.Streak);
        Assert.False(Get(progress, MilestoneEvaluator.FirstTuneUp).Achieved);
    }

    [Fact]
    public void Evaluate_FreedSpaceAndChanges_AreSummedAcrossKeptSessions()
    {
        var startup = Session(Now.AddDays(-3), new StartupItemToggledEntry("Steam", "RunKeyCurrentUser", true) { Completed = true, At = Now.AddDays(-3) });

        var progress = Evaluate([Cleaned(Now.AddDays(-1), 6L * 1024 * 1024 * 1024), Cleaned(Now.AddDays(-2), 5L * 1024 * 1024 * 1024), startup]);

        Assert.Equal(11L * 1024 * 1024 * 1024, progress.FreedBytes);
        Assert.Equal(1, progress.Changes);
        Assert.True(Get(progress, MilestoneEvaluator.TenGigabytes).Achieved);
        Assert.True(Get(progress, MilestoneEvaluator.SpeedTuneUp).Achieved);
    }

    [Fact]
    public void Evaluate_StartupMilestones_ComeFromWhatWindowsMeasured()
    {
        var changedAt = Now.AddDays(-3);
        var startup = Session(changedAt, new StartupItemToggledEntry("Steam", "RunKeyCurrentUser", true) { Completed = true, At = changedAt });
        var performance = new BootPerformance(
            [
                new BootRecord(Now.AddDays(-5), TimeSpan.FromSeconds(50)),
                new BootRecord(Now.AddDays(-1), TimeSpan.FromSeconds(38)),
            ],
            []);

        var progress = Evaluate([startup], performance, startupAppsOn: 12);

        Assert.Equal(TimeSpan.FromSeconds(12), progress.StartupSaved);
        Assert.True(Get(progress, MilestoneEvaluator.TenSecondsFaster).Achieved);
        Assert.True(Get(progress, MilestoneEvaluator.UnderFortySeconds).Achieved);
        Assert.False(Get(progress, MilestoneEvaluator.LeanStartup).Achieved);
        Assert.Equal(0.5, Get(progress, MilestoneEvaluator.LeanStartup).Progress);
    }

    [Fact]
    public void Evaluate_SlowStartNotYetUnderForty_ShowsHowCloseItIs()
    {
        var performance = new BootPerformance([new BootRecord(Now.AddDays(-1), TimeSpan.FromSeconds(50))], []);

        var progress = Evaluate([], performance, startupAppsOn: 0);

        Assert.Equal(0.8, Get(progress, MilestoneEvaluator.UnderFortySeconds).Progress);
        Assert.Null(progress.StartupSaved);
        Assert.Null(Get(progress, MilestoneEvaluator.TenSecondsFaster).Progress);
        Assert.True(Get(progress, MilestoneEvaluator.LeanStartup).Achieved);
    }
}
