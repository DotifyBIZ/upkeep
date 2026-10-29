using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class MilestonesViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-milestones-vm-{Guid.NewGuid():N}");
    private readonly FakeStartupItemScanner _startupScanner = new();
    private readonly FakeBootPerformanceSource _bootPerformance = new();
    private readonly SessionJournal _journal;

    public MilestonesViewModelTests() => _journal = new SessionJournal(_journalDirectory, new FixedTimeProvider(Now));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _journal.Dispose();

        try
        {
            Directory.Delete(_journalDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private MilestonesViewModel CreateViewModel() =>
        new(_journal, _startupScanner, _bootPerformance, new FakeLocalizationService(), new FakeAppLogger(), new FixedTimeProvider(Now));

    [Fact]
    public async Task LoadAsync_NothingDoneYet_ShowsAnOpenMonthAndNothingReached()
    {
        // More than a lean startup's worth, so that milestone isn't reached either.
        for (int index = 0; index < 7; index++)
        {
            _startupScanner.Items.Add(new StartupItem { Id = $"a{index}", Name = $"a{index}", Source = StartupSource.RunKeyCurrentUser });
        }

        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("MilestonesStreakFormat(0)", viewModel.StreakHeadline);
        Assert.Equal("MilestonesRhythmOpen", viewModel.RhythmNote);
        Assert.Equal(6, viewModel.Months.Count);
        Assert.True(viewModel.Months[^1].IsOpen);
        Assert.True(viewModel.Months[0].IsMissed);
        Assert.Equal("MilestonesAchievedFormat(0|8)", viewModel.AchievedSummary);
        Assert.Equal("—", viewModel.StartupSavedDisplay);
        Assert.Equal([false], _bootPerformance.Reads);
    }

    [Fact]
    public async Task LoadAsync_SessionThisMonth_CountsItAndReachesTheFirstMilestone()
    {
        var session = await _journal.StartAsync(SessionKind.Cleanup, CancellationToken.None);
        await _journal.AppendAsync(session, new FileDeletedEntry(@"C:\t.tmp", 2048) { Completed = true }, CancellationToken.None);
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("MilestonesStreakFormat(1)", viewModel.StreakHeadline);
        Assert.Equal("MilestonesRhythmDone", viewModel.RhythmNote);
        Assert.True(viewModel.Months[^1].Done);
        Assert.True(viewModel.Milestones[0].Achieved);
        Assert.Equal("MilestoneFirstTuneUpTitle", viewModel.Milestones[0].Title);
        Assert.Equal("0", viewModel.ChangesDisplay);
    }

    [Fact]
    public async Task LoadAsync_StartupMilestonesWithoutTimings_SayToRunSpeedCheck()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        var underForty = viewModel.Milestones.Single(milestone => milestone.Title == "MilestoneUnderFortySecondsTitle");
        Assert.Equal("MilestoneNeedsSpeedCheck", underForty.Detail);
        Assert.False(underForty.ShowsProgress);
        Assert.True(underForty.IsLocked);
    }

    [Fact]
    public async Task LoadAsync_WithTimings_ShowsTheMeasuredSavingAndProgress()
    {
        var session = await _journal.StartAsync(SessionKind.Startup, CancellationToken.None);
        await _journal.AppendAsync(session, new StartupItemToggledEntry("Steam", "RunKeyCurrentUser", true) { Completed = true, At = Now.AddDays(-3) }, CancellationToken.None);
        _bootPerformance.Available = new BootPerformance(
            [new BootRecord(Now.AddDays(-5), TimeSpan.FromSeconds(58)), new BootRecord(Now.AddDays(-1), TimeSpan.FromSeconds(50))],
            []);
        _bootPerformance.AlreadyElevated = true;
        for (int index = 0; index < 12; index++)
        {
            _startupScanner.Items.Add(new StartupItem { Id = $"a{index}", Name = $"a{index}", Source = StartupSource.RunKeyCurrentUser });
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("CommonSecondsFormat(8)", viewModel.StartupSavedDisplay);
        Assert.Equal("MilestonesStartupSavedCaption", viewModel.StartupSavedCaption);
        var lean = viewModel.Milestones.Single(milestone => milestone.Title == "MilestoneLeanStartupTitle");
        Assert.True(lean.ShowsProgress);
        Assert.Equal(50, lean.ProgressPercent);
    }
}
