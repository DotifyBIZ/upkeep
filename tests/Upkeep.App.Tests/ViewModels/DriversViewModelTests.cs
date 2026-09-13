using Upkeep.App.Core.Drivers;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Updates;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class DriversViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-drivers-vm-{Guid.NewGuid():N}");
    private readonly FakeDriverScanner _scanner = new();
    private readonly FakeElevationService _elevation = new();
    private readonly FakeWindowsUiLauncher _launcher = new();
    private readonly FakeRegistryReader _registry = new();
    private readonly SessionJournal _journal;

    public DriversViewModelTests() => _journal = new SessionJournal(_journalDirectory);

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

    private DriversViewModel CreateViewModel(WindowsEditionKind edition = WindowsEditionKind.Professional) => new(
        _scanner,
        _elevation,
        _launcher,
        _registry,
        new WindowsEditionInfo(edition.ToString(), edition),
        _journal,
        new FakeLocalizationService(),
        new FakeAppLogger(),
        new FixedTimeProvider(Now));

    private async Task<DriversViewModel> LoadedAsync(WindowsEditionKind edition = WindowsEditionKind.Professional)
    {
        var viewModel = CreateViewModel(edition);
        await viewModel.LoadAsync(CancellationToken.None);
        return viewModel;
    }

    private void GiveTwoDrivers()
    {
        _scanner.Drivers.Add(new DriverInfo("NVIDIA GeForce RTX 4070", "NVIDIA", "31.0.15.3623", Now.AddYears(-1), "PCI", "Display"));
        _scanner.Drivers.Add(new DriverInfo("Realtek Audio", "Realtek", "6.0.9285.1", null, "HDAUDIO", "MEDIA"));
    }

    /// <summary>Records a live pause and a deferral pair, the way Windows spells them.</summary>
    private void GivePausedUpdates(int daysLeft = 5, int featureDays = 180, int qualityDays = 7)
    {
        _registry.Strings[(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseExpiryValueName)] =
            WindowsUpdatePolicy.FormatTime(Now.AddDays(daysLeft));

        _registry.Integers[(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName)] = featureDays;
        _registry.Integers[(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.QualityDeferValueName)] = qualityDays;
    }

    [Fact]
    public async Task LoadAsync_ListsWhatThisPcIsRunning()
    {
        GiveTwoDrivers();

        var viewModel = await LoadedAsync();

        Assert.Equal(2, viewModel.Drivers.Count);
        Assert.False(viewModel.IsEmpty);
        Assert.Contains("31.0.15.3623", viewModel.Drivers[0].VersionDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_DriverWithNoRecordedDate_ShowsJustTheVersion()
    {
        GiveTwoDrivers();

        var viewModel = await LoadedAsync();

        Assert.Equal("6.0.9285.1", viewModel.Drivers[1].VersionDisplay);
    }

    [Fact]
    public async Task LoadAsync_NoDrivers_SaysSoRatherThanShowingAnEmptyTable()
    {
        var viewModel = await LoadedAsync();

        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_ShowsHowUpdatesAreScheduledWithoutAskingForAdministrator()
    {
        // Reading what Windows is set to must not cost an elevation prompt, or a write. Only
        // changing it does.
        GivePausedUpdates();

        var viewModel = await LoadedAsync();

        Assert.True(viewModel.IsPaused);
        Assert.Equal(180, viewModel.FeatureDeferralDays);
        Assert.Equal(7, viewModel.QualityDeferralDays);
        Assert.Empty(_elevation.SentRequests);
    }

    [Fact]
    public async Task LoadAsync_PauseThatHasAlreadyExpired_ReadsAsNotPaused()
    {
        // Windows clears these values lazily, so a stale expiry must not show as a live pause.
        _registry.Strings[(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseExpiryValueName)] =
            WindowsUpdatePolicy.FormatTime(Now.AddDays(-1));

        var viewModel = await LoadedAsync();

        Assert.False(viewModel.IsPaused);
        Assert.Null(viewModel.PausedUntilDisplay);
    }

    [Fact]
    public async Task Load_OnHome_HidesTheDeferralControls()
    {
        // Home reads the deferral policies and ignores them (ADR-0009).
        var viewModel = await LoadedAsync(WindowsEditionKind.Home);

        Assert.False(viewModel.SupportsDeferral);
    }

    [Fact]
    public async Task Load_OnPro_OffersTheDeferralControls()
    {
        var viewModel = await LoadedAsync();

        Assert.True(viewModel.SupportsDeferral);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_MarksOnlyTheDeviceTheUpdateNames()
    {
        GiveTwoDrivers();
        _elevation.DriverUpdates.Add(new DriverUpdate("NVIDIA - Display - NVIDIA GeForce RTX 4070", "NVIDIA GeForce RTX 4070"));

        var viewModel = await LoadedAsync();
        await viewModel.CheckForUpdatesAsync(CancellationToken.None);

        Assert.True(viewModel.Drivers[0].HasUpdate);
        Assert.False(viewModel.Drivers[1].HasUpdate);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SearchesButNeverInstalls()
    {
        // Installing is handed to Windows (ADR-0009); the helper is only ever asked to look.
        GiveTwoDrivers();

        var viewModel = await LoadedAsync();
        await viewModel.CheckForUpdatesAsync(CancellationToken.None);

        Assert.IsType<SearchDriverUpdatesRequest>(Assert.Single(_elevation.SentRequests));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WindowsUpdateCouldNotBeAsked_ShowsTheInstalledVersionsAnyway()
    {
        GiveTwoDrivers();
        _elevation.DriverSearchSucceeded = false;

        var viewModel = await LoadedAsync();
        await viewModel.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal("DriversSearchFailed", viewModel.SearchSummary);
        Assert.Equal(2, viewModel.Drivers.Count);
        Assert.All(viewModel.Drivers, driver => Assert.Null(driver.HasUpdate));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ElevationDeclined_AsksWindowsUpdateNothing()
    {
        GiveTwoDrivers();
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);

        var viewModel = await LoadedAsync();
        await viewModel.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal("CommonElevationDeclined", viewModel.StatusMessage);
        Assert.Empty(_elevation.SentRequests);
    }

    [Fact]
    public async Task PauseUpdatesAsync_JournalsWhatToGoBackToBeforeChangingAnything()
    {
        GivePausedUpdates(daysLeft: 3);

        var viewModel = await LoadedAsync();
        viewModel.PauseDays = 14;
        await viewModel.PauseUpdatesAsync(CancellationToken.None);

        var entry = Assert.IsType<SystemSettingChangedEntry>(Assert.Single(await LatestEntriesAsync()));

        Assert.Equal(UpdateSettingIds.Pause, entry.SettingId);
        Assert.Equal(WindowsUpdatePolicy.FormatTime(Now.AddDays(3)), entry.PreviousValue);
        Assert.True(entry.Completed);
    }

    [Fact]
    public async Task PauseUpdatesAsync_SendsTheDaysTheUserChose()
    {
        var viewModel = await LoadedAsync();
        viewModel.PauseDays = 14;
        await viewModel.PauseUpdatesAsync(CancellationToken.None);

        Assert.Equal(14, Assert.IsType<SetUpdatePauseRequest>(Assert.Single(_elevation.SentRequests)).Days);
    }

    [Fact]
    public async Task PauseUpdatesAsync_UpdatesWereNotPaused_RecordsThatRatherThanAnInventedExpiry()
    {
        var viewModel = await LoadedAsync();
        await viewModel.PauseUpdatesAsync(CancellationToken.None);

        var entry = Assert.IsType<SystemSettingChangedEntry>(Assert.Single(await LatestEntriesAsync()));

        Assert.Equal(string.Empty, entry.PreviousValue);
    }

    [Fact]
    public async Task PauseUpdatesAsync_WindowsRefused_LeavesTheEntryUnfinished()
    {
        // An entry that never completed is how History shows what did not happen.
        _elevation.UpdateChangeSucceeds = false;

        var viewModel = await LoadedAsync();
        await viewModel.PauseUpdatesAsync(CancellationToken.None);

        var entry = Assert.IsType<SystemSettingChangedEntry>(Assert.Single(await LatestEntriesAsync()));

        Assert.False(entry.Completed);
        Assert.Equal("refused", entry.FailureDetail);
        Assert.Equal("UpdatesChangeRefused", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PauseUpdatesAsync_ElevationDeclined_WritesNoJournalEntryAtAll()
    {
        // Nothing was changed, so there is nothing to put back.
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);

        var viewModel = await LoadedAsync();
        await viewModel.PauseUpdatesAsync(CancellationToken.None);

        Assert.Empty(await LatestEntriesAsync());
        Assert.Equal("CommonElevationDeclined", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ResumeUpdatesAsync_EndsThePauseRatherThanShorteningIt()
    {
        GivePausedUpdates();

        var viewModel = await LoadedAsync();
        await viewModel.ResumeUpdatesAsync(CancellationToken.None);

        Assert.Equal(0, Assert.IsType<SetUpdatePauseRequest>(Assert.Single(_elevation.SentRequests)).Days);
    }

    [Fact]
    public async Task ApplyDeferralAsync_JournalsThePeriodsAsTheyStood()
    {
        GivePausedUpdates(featureDays: 180, qualityDays: 7);

        var viewModel = await LoadedAsync();
        viewModel.FeatureDeferralDays = 30;
        viewModel.QualityDeferralDays = 0;
        await viewModel.ApplyDeferralAsync(CancellationToken.None);

        var entry = Assert.IsType<SystemSettingChangedEntry>(Assert.Single(await LatestEntriesAsync()));

        Assert.Equal(UpdateSettingIds.Deferral, entry.SettingId);
        Assert.Equal(WindowsUpdatePolicy.FormatDeferral(180, 7), entry.PreviousValue);
        Assert.Equal(WindowsUpdatePolicy.FormatDeferral(30, 0), entry.NewValue);
    }

    [Fact]
    public async Task ApplyDeferralAsync_MoreThanWindowsHonours_RecordsWhatWillActuallyBeSet()
    {
        var viewModel = await LoadedAsync();
        viewModel.FeatureDeferralDays = 4000;
        viewModel.QualityDeferralDays = 90;
        await viewModel.ApplyDeferralAsync(CancellationToken.None);

        var entry = Assert.IsType<SystemSettingChangedEntry>(Assert.Single(await LatestEntriesAsync()));

        Assert.Equal(
            WindowsUpdatePolicy.FormatDeferral(WindowsUpdatePolicy.MaximumFeatureDeferralDays, WindowsUpdatePolicy.MaximumQualityDeferralDays),
            entry.NewValue);
    }

    [Fact]
    public async Task ApplyDeferralAsync_TakesTheScheduleBackFromTheHelperNotFromWhatWasAsked()
    {
        // Windows clamps these, so the page shows what was actually written.
        _elevation.UpdateState = new WindowsUpdateState(null, 365, 30);

        var viewModel = await LoadedAsync();
        viewModel.FeatureDeferralDays = 4000;
        await viewModel.ApplyDeferralAsync(CancellationToken.None);

        Assert.Equal(365, viewModel.FeatureDeferralDays);
        Assert.Equal(30, viewModel.QualityDeferralDays);
    }

    [Fact]
    public async Task Changes_AcrossOnePageVisit_ShareOneSession()
    {
        var viewModel = await LoadedAsync();
        await viewModel.PauseUpdatesAsync(CancellationToken.None);
        await viewModel.ApplyDeferralAsync(CancellationToken.None);

        Assert.Equal(2, (await LatestEntriesAsync()).Count);
    }

    [Fact]
    public async Task OpenWindowsUpdate_HandsTheInstallToWindows()
    {
        var viewModel = await LoadedAsync();
        viewModel.OpenWindowsUpdateCommand.Execute(null);

        Assert.Equal(WindowsUiLauncher.WindowsUpdate, Assert.Single(_launcher.Opened).Target);
    }

    [Fact]
    public async Task OpenDeviceManager_HandsTheRollbackToWindows()
    {
        var viewModel = await LoadedAsync();
        viewModel.OpenDeviceManagerCommand.Execute(null);

        Assert.Equal(WindowsUiLauncher.DeviceManager, Assert.Single(_launcher.Opened).Target);
    }

    [Fact]
    public async Task StatusDisplay_BeforeASearch_SaysNotCheckedRatherThanUpToDate()
    {
        GiveTwoDrivers();

        var viewModel = await LoadedAsync();

        Assert.Equal("DriversStatusUnknown", viewModel.Drivers[0].StatusDisplay);
    }

    [Fact]
    public async Task StatusDisplay_AfterASearch_ReadsAsLocalizedTextNotAResourceKey()
    {
        GiveTwoDrivers();
        _elevation.DriverUpdates.Add(new DriverUpdate("NVIDIA GeForce RTX 4070 driver", null));

        var viewModel = await LoadedAsync();
        await viewModel.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal("DriversStatusUpdateAvailable", viewModel.Drivers[0].StatusDisplay);
        Assert.Equal("DriversStatusUpToDate", viewModel.Drivers[1].StatusDisplay);
    }

    /// <summary>The entries of the one session this page opened, read back off disk.</summary>
    private async Task<IReadOnlyList<SessionEntry>> LatestEntriesAsync()
    {
        var sessions = await _journal.ListAsync(CancellationToken.None);
        return sessions.Count == 0 ? [] : sessions[0].Entries;
    }
}
