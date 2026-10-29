using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class SpeedCheckViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChangedAt = Now.AddDays(-2);

    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-speed-vm-{Guid.NewGuid():N}");
    private readonly FakeBootPerformanceSource _bootPerformance = new();
    private readonly FakeStartupItemScanner _startupScanner = new();
    private readonly FakeLaunchHistory _launches = new();
    private readonly FakePowerModeSettings _powerMode = new();
    private readonly FakeBackgroundAppSettings _backgroundApps = new();
    private readonly FakePerformanceSettings _performance = new();
    private readonly SessionJournal _journal;

    public SpeedCheckViewModelTests()
    {
        _journal = new SessionJournal(_journalDirectory);
        _performance.State = new PerformanceState(AnimationsEnabled: false, TransparencyEnabled: true, PowerPlans: []);
    }

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

    private SpeedCheckViewModel CreateViewModel() => new(
        _bootPerformance,
        _journal,
        _startupScanner,
        _launches,
        _powerMode,
        _backgroundApps,
        _performance,
        new FakeLocalizationService(),
        new FakeAppLogger(),
        new FixedTimeProvider(Now));

    private static BootRecord Boot(double daysAgo, double seconds) => new(Now.AddDays(-daysAgo), TimeSpan.FromSeconds(seconds));

    private void Measured(params BootRecord[] boots) => Measured(boots, []);

    private void Measured(BootRecord[] boots, SlowStartupApp[] apps)
    {
        _bootPerformance.Available = new BootPerformance(boots, apps);
        _bootPerformance.AlreadyElevated = true;
    }

    private async Task ChangedStartupAsync()
    {
        var session = await _journal.StartAsync(SessionKind.Startup, CancellationToken.None);
        await _journal.AppendAsync(session, new StartupItemToggledEntry("Steam", "RunKeyCurrentUser", true) { Completed = true, At = ChangedAt }, CancellationToken.None);
    }

    [Fact]
    public async Task LoadAsync_NotElevated_AsksForNothingAndOffersToMeasure()
    {
        _bootPerformance.Available = new BootPerformance([Boot(1, 40)], []);
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal([false], _bootPerformance.Reads);
        Assert.True(viewModel.NeedsMeasurement);
        Assert.False(viewModel.HasLastBoot);
    }

    [Fact]
    public async Task MeasureAsync_ShowsTheLastStartAndChartsTheRecentOnesOldestFirst()
    {
        _bootPerformance.Available = new BootPerformance([Boot(0.5, 48), Boot(1, 44), Boot(2, 40)], []);
        var viewModel = CreateViewModel();

        await viewModel.MeasureAsync(CancellationToken.None);

        Assert.True(viewModel.IsMeasured);
        Assert.True(viewModel.IsNotMeasuring);
        Assert.Equal("CommonSecondsFormat(48)", viewModel.LastBootDisplay);
        Assert.Contains("|3|CommonSecondsFormat(44)", viewModel.LastBootCaption, StringComparison.Ordinal);
        Assert.Equal(3, viewModel.Bars.Count);
        Assert.Equal(SpeedCheckViewModel.ChartHeight, viewModel.Bars[2].Height);
        Assert.True(viewModel.Bars[2].IsLatest);
        Assert.True(viewModel.Bars[0].IsEarlier);
        Assert.False(viewModel.HasComparison);
        Assert.False(viewModel.IsWaitingForRestart);
    }

    [Fact]
    public async Task MeasureAsync_NoStartOnRecord_SaysSo()
    {
        _bootPerformance.Available = BootPerformance.Empty;
        var viewModel = CreateViewModel();

        await viewModel.MeasureAsync(CancellationToken.None);

        Assert.True(viewModel.HasNoBoots);
        Assert.Empty(viewModel.Bars);
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
        Assert.True(viewModel.HasStatusMessage);
    }

    [Fact]
    public async Task LoadAsync_ChangedAndRestarted_ShowsWhatWindowsMeasuredBeforeAndAfter()
    {
        await ChangedStartupAsync();
        Measured(Boot(5, 46), Boot(4, 44), Boot(3, 48), Boot(1, 35), Boot(0.5, 33));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasComparison);
        Assert.Equal("SpeedCheckFasterFormat(CommonSecondsFormat(12))", viewModel.ComparisonHeadline);
        Assert.Contains("SpeedCheckSmallSample", viewModel.ComparisonDetail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(52, "SpeedCheckSlowerFormat(CommonSecondsFormat(6))")]
    [InlineData(46.4, "SpeedCheckAboutTheSame")]
    public async Task LoadAsync_SlowerOrUnchanged_SaysSoHonestly(double after, string expected)
    {
        await ChangedStartupAsync();
        Measured(Boot(5, 46), Boot(1, after), Boot(0.8, after), Boot(0.5, after));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, viewModel.ComparisonHeadline);
        Assert.DoesNotContain("SpeedCheckSmallSample", viewModel.ComparisonDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ChangedButNotRestarted_AsksForARestart()
    {
        await ChangedStartupAsync();
        Measured(Boot(5, 46), Boot(4, 44));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasComparison);
        Assert.True(viewModel.IsWaitingForRestart);
        Assert.StartsWith("SpeedCheckWaitingForRestartFormat", viewModel.WaitingForRestartMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RecommendsWhatWouldHelpWithoutPromisingSeconds()
    {
        _startupScanner.Items.Add(new StartupItem { Id = "Steam", Name = "Steam", Command = @"C:\Steam\steam.exe", Source = StartupSource.RunKeyCurrentUser });
        Measured([Boot(1, 40)], [new SlowStartupApp(Now.AddDays(-1), "steam.exe", null, TimeSpan.FromSeconds(2.6), TimeSpan.FromSeconds(1))]);
        _powerMode.State = new PowerModeState(PowerMode.BestPowerEfficiency, PowerMode.Balanced, AppliesToActivePlan: true, HasBattery: true);
        _performance.State = new PerformanceState(AnimationsEnabled: true, TransparencyEnabled: true, PowerPlans: []);
        _backgroundApps.Apps.Add(new BackgroundApp("A_abcdefghjkmnp", "A", BackgroundAccess.PowerOptimized));
        _backgroundApps.Apps.Add(new BackgroundApp("B_abcdefghjkmnp", "B", BackgroundAccess.Never));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasRecommendations);
        Assert.Equal(["Startup", "Performance", "Performance", "Performance"], viewModel.Recommendations.Select(item => item.TargetTab));
        Assert.Equal("SpeedCheckStartupTitleFormat(1)", viewModel.Recommendations[0].Title);
        Assert.Equal("SpeedCheckBackgroundTitleFormat(1)", viewModel.Recommendations[3].Title);
    }

    [Fact]
    public async Task LoadAsync_NothingStandsOut_RecommendsNothing()
    {
        Measured(Boot(1, 40));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasRecommendations);
    }

    [Fact]
    public async Task LoadAsync_NotMeasured_StillRecommendsSettingsButNotStartupApps()
    {
        _startupScanner.Items.Add(new StartupItem { Id = "Steam", Name = "Steam", Command = @"C:\Steam\steam.exe", Source = StartupSource.RunKeyCurrentUser });
        _powerMode.State = new PowerModeState(PowerMode.BestPowerEfficiency, PowerMode.Balanced, AppliesToActivePlan: true, HasBattery: true);
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("SpeedCheckPowerModeTitle", Assert.Single(viewModel.Recommendations).Title);
    }
}
