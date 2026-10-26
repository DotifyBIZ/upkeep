using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class StartupViewModelTests : IDisposable
{
    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-startup-vm-{Guid.NewGuid():N}");
    private readonly FakeStartupItemScanner _scanner = new();
    private readonly FakeStartupItemToggler _toggler = new();
    private readonly SessionJournal _journal;

    public StartupViewModelTests() => _journal = new SessionJournal(_journalDirectory);

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

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeBootPerformanceSource _bootPerformance = new();
    private readonly FakeLaunchHistory _launches = new();

    private StartupViewModel CreateViewModel() =>
        new(_scanner, _toggler, _journal, _bootPerformance, _launches, new FakeLocalizationService(), new FakeAppLogger(), new FixedTimeProvider(Now));

    private static SlowStartupApp Slow(string file, int totalMs) =>
        new(Now.AddDays(-1), file, null, TimeSpan.FromMilliseconds(totalMs), TimeSpan.FromMilliseconds(totalMs / 2));

    /// <summary>Discord and Steam, both timed; Steam slower and not started by the person in 40 days.</summary>
    private async Task<StartupViewModel> MeasuredAsync()
    {
        _scanner.Items.Add(Item());
        _scanner.Items.Add(Item("Steam") with { Command = "\"C:\\Steam\\steam.exe\" -silent" });
        _bootPerformance.Available = new BootPerformance([], [Slow("Update.exe", 1200), Slow("steam.exe", 2400)]);
        _bootPerformance.AlreadyElevated = true;
        _launches.Launches["discord"] = Now.AddDays(-2);
        _launches.Launches["steam"] = Now.AddDays(-40);

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);
        return viewModel;
    }

    [Fact]
    public async Task LoadAsync_NotElevated_NeverPromptsAndOffersToMeasure()
    {
        _scanner.Items.Add(Item());
        _bootPerformance.Available = new BootPerformance([], []);
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal([false], _bootPerformance.Reads);
        Assert.True(viewModel.NeedsMeasurement);
        Assert.Equal("—", viewModel.Items[0].TimingDisplay);
        Assert.Equal("StartupTimingNotMeasuredTooltip", viewModel.Items[0].TimingTooltip);
    }

    [Fact]
    public async Task LoadAsync_Measured_RanksSlowestFirstWithWhatWindowsRecorded()
    {
        var viewModel = await MeasuredAsync();

        Assert.True(viewModel.IsMeasured);
        Assert.Equal(["Steam", "Discord"], viewModel.Items.Select(item => item.Name));
        Assert.Contains("2", viewModel.Items[0].TimingDisplay, StringComparison.Ordinal);
        Assert.Equal(StartupItemDisplay.TimingBarMaxWidth, viewModel.Items[0].TimingBarWidth);
        Assert.Equal(StartupItemDisplay.TimingBarMaxWidth / 2, viewModel.Items[1].TimingBarWidth);
        Assert.Equal("StartupLastStartedDaysAgoFormat(40)", viewModel.Items[0].LastStartedDisplay);
        Assert.True(viewModel.Items[0].IsStale);
        Assert.False(viewModel.Items[1].IsStale);
    }

    [Fact]
    public async Task LoadAsync_AppWindowsDidNotTime_SaysNotTimedAndWhy()
    {
        _scanner.Items.Add(Item("Quiet") with { Command = @"C:\q\quiet.exe" });
        _bootPerformance.Available = new BootPerformance([], []);
        _bootPerformance.AlreadyElevated = true;
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("StartupNotTimed", viewModel.Items[0].TimingDisplay);
        Assert.Equal("StartupNotTimedTooltip", viewModel.Items[0].TimingTooltip);
        Assert.Equal("StartupLastStartedNever", viewModel.Items[0].LastStartedDisplay);
        Assert.False(viewModel.HasSuggestions);
    }

    [Theory]
    [InlineData(0, "StartupLastStartedToday")]
    [InlineData(1, "StartupLastStartedYesterday")]
    public async Task LoadAsync_RecentLaunches_ReadAsWords(int daysAgo, string expected)
    {
        _scanner.Items.Add(Item());
        _launches.Launches["discord"] = Now.AddDays(-daysAgo);
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, viewModel.Items[0].LastStartedDisplay);
    }

    [Fact]
    public async Task MeasureAsync_AsksForApprovalAndRanksWhatItRead()
    {
        _scanner.Items.Add(Item());
        _bootPerformance.Available = new BootPerformance([], [Slow("Update.exe", 1200)]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.MeasureAsync(CancellationToken.None);

        Assert.Equal([false, true], _bootPerformance.Reads);
        Assert.True(viewModel.IsMeasured);
        Assert.Null(viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(BootPerformanceStatus.Declined, "CommonElevationDeclined")]
    [InlineData(BootPerformanceStatus.Failed, "SpeedCheckReadFailed")]
    public async Task MeasureAsync_NotRead_SaysWhy(BootPerformanceStatus status, string expected)
    {
        _bootPerformance.Status = status;
        var viewModel = CreateViewModel();

        await viewModel.MeasureAsync(CancellationToken.None);

        Assert.Equal(expected, viewModel.StatusMessage);
        Assert.False(viewModel.IsMeasured);
    }

    [Fact]
    public async Task PreparePlan_PreTicksOnlyWhatIsSuggested()
    {
        var viewModel = await MeasuredAsync();

        viewModel.PreparePlan();

        Assert.Equal(1, viewModel.SuggestedCount);
        var line = Assert.Single(viewModel.PlanItems);
        Assert.Equal("Steam", line.Name);
        Assert.True(line.IsSelected);
    }

    [Fact]
    public async Task ApplyPlanAsync_TurnsOffOnlyWhatStayedTickedAndJournalsIt()
    {
        _scanner.Items.Add(Item("Epic") with { Command = @"C:\e\EpicGamesLauncher.exe" });
        _launches.Launches["epicgameslauncher"] = Now.AddDays(-90);
        var viewModel = await MeasuredAsync();

        // Epic wasn't timed, so it isn't suggested; Steam is, and the person keeps it ticked.
        viewModel.PreparePlan();
        await viewModel.ApplyPlanAsync(CancellationToken.None);

        Assert.Equal(("Steam", false), Assert.Single(_toggler.Changes));
        Assert.False(viewModel.Items.Single(item => item.Name == "Steam").IsEnabled);
        Assert.Empty(viewModel.PlanItems);
        Assert.False(viewModel.HasSuggestions);
        Assert.Single(await _journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyPlanAsync_UntickedLine_IsLeftAlone()
    {
        var viewModel = await MeasuredAsync();
        viewModel.PreparePlan();
        viewModel.PlanItems[0].IsSelected = false;

        await viewModel.ApplyPlanAsync(CancellationToken.None);

        Assert.Empty(_toggler.Changes);
    }

    [Fact]
    public async Task ApplyAsync_SwitchMovedBackByUpkeep_IsNotAppliedAgain()
    {
        // Toggled fires when Upkeep moves a switch too; matching what Windows has means no change.
        _scanner.Items.Add(Item());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        Assert.Empty(_toggler.Changes);
    }

    private static StartupItem Item(
        string id = "Discord",
        StartupSource source = StartupSource.RunKeyCurrentUser,
        bool isEnabled = true,
        bool requiresElevation = false) => new()
        {
            Id = id,
            Name = id,
            Publisher = "Discord Inc.",
            Command = @"C:\Users\a\AppData\Local\Discord\Update.exe",
            Source = source,
            IsEnabled = isEnabled,
            RequiresElevation = requiresElevation,
        };

    [Fact]
    public async Task LoadAsync_ShowsEveryStartupItem()
    {
        _scanner.Items.Add(Item());
        _scanner.Items.Add(Item("OneDrive", StartupSource.RunKeyAllUsers, requiresElevation: true));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.Items[1].RequiresElevation);
    }

    [Fact]
    public async Task LoadAsync_LabelsWhereEachItemStartsFrom()
    {
        _scanner.Items.Add(Item(source: StartupSource.ScheduledTask));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("StartupSourceScheduledTask", viewModel.Items[0].SourceLabel);
    }

    [Fact]
    public async Task LoadAsync_NothingStartsWithWindows_SaysSo()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_RunOnceEntry_CannotBeToggled()
    {
        // It deletes itself after running; a switch would promise something that isn't true.
        _scanner.Items.Add(Item("Finish setup", StartupSource.RunOnce));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Items[0].CanToggle);
    }

    [Fact]
    public async Task LoadAsync_DisabledItem_ShowsWhenItWasTurnedOff()
    {
        _scanner.Items.Add(Item(isEnabled: false) with { DisabledAt = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero) });
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Items[0].IsEnabled);
        Assert.Contains("StartupDisabledAtFormat", viewModel.Items[0].DisabledAtDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_PassesTheChangeThroughAndOpensASession()
    {
        _scanner.Items.Add(Item());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        Assert.Equal(("Discord", false), _toggler.Changes.Single());
        Assert.Single(await _journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_WindowsRefused_PutsTheSwitchBack()
    {
        // A switch showing a state that never took effect is worse than an error message.
        _scanner.Items.Add(Item());
        _toggler.Succeeds = false;
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        Assert.True(viewModel.Items[0].IsEnabled);
        Assert.Contains("StartupChangeRefusedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_SeveralChanges_ShareOneSession()
    {
        // One visit to the page is one session, so History can revert the visit as a whole.
        _scanner.Items.Add(Item());
        _scanner.Items.Add(Item("Steam"));
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);
        viewModel.Items[1].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[1], CancellationToken.None);

        Assert.Single(await _journal.ListAsync(CancellationToken.None));
        Assert.Equal(2, _toggler.Changes.Count);
    }

    [Fact]
    public async Task EndSessionAsync_ClosesTheSessionForHistory()
    {
        _scanner.Items.Add(Item());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        await viewModel.EndSessionAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task EndSessionAsync_NothingWasChanged_WritesNoSession()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.EndSessionAsync(CancellationToken.None);

        Assert.Empty(await _journal.ListAsync(CancellationToken.None));
    }
}
