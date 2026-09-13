using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Drivers;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Updates;

namespace Upkeep.App.ViewModels;

/// <summary>One installed driver, as the page shows it.</summary>
public sealed partial class DriverDisplay : ObservableObject
{
    private readonly ILocalizationService _localization;

    public DriverDisplay(DriverInfo driver, string versionDisplay, ILocalizationService localization)
    {
        Driver = driver;
        VersionDisplay = versionDisplay;
        _localization = localization;
    }

    public DriverInfo Driver { get; }

    public string DeviceName => Driver.DeviceName;

    /// <summary>Publisher where Windows recorded one — enough to recognize a cryptic device.</summary>
    public string Subtitle => Driver.Manufacturer ?? Driver.DeviceClass ?? string.Empty;

    /// <summary>Installed version, with the driver date where Windows recorded one.</summary>
    public string VersionDisplay { get; }

    /// <summary>
    /// Whether Windows Update is offering something newer. Null until a search has run: "not
    /// checked" is a different answer from "up to date".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    public partial bool? HasUpdate { get; set; }

    /// <summary>Localized status text. "Not checked" until a search has run.</summary>
    public string StatusDisplay => _localization.GetString(HasUpdate switch
    {
        true => "DriversStatusUpdateAvailable",
        false => "DriversStatusUpToDate",
        _ => "DriversStatusUnknown",
    });
}

/// <summary>
/// The Drivers &amp; updates tab: what this PC is running, what Windows Update has newer, and how
/// updates are scheduled.
/// <para>
/// Upkeep searches but never installs (ADR-0009): installing is handed to Windows, which already
/// knows how to finish after a restart, and rolling back is handed to Device Manager, which keeps
/// the previous driver package. Pausing and deferring are machine-wide writes, so they go through
/// the elevated helper.
/// </para>
/// </summary>
public sealed partial class DriversViewModel : ObservableObject
{
    private readonly IDriverScanner _scanner;
    private readonly IElevationService _elevation;
    private readonly IWindowsUiLauncher _launcher;
    private readonly IRegistryProbe _registry;
    private readonly ILocalizationService _localization;
    private readonly ISessionJournal _journal;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>The session this page has opened, if it has changed anything yet.</summary>
    private SessionManifest? _session;

    /// <summary>The schedule as last read back, which is what a journal entry has to record.</summary>
    private WindowsUpdateState _current = WindowsUpdateState.NotConfigured;

    public DriversViewModel(
        IDriverScanner scanner,
        IElevationService elevation,
        IWindowsUiLauncher launcher,
        IRegistryProbe registry,
        WindowsEditionInfo edition,
        ISessionJournal journal,
        ILocalizationService localization,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(edition);

        _scanner = scanner;
        _elevation = elevation;
        _launcher = launcher;
        _registry = registry;
        _journal = journal;
        _localization = localization;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Home reads the deferral policies and ignores them, so the controls are hidden rather
        // than shown doing nothing (ADR-0009).
        SupportsDeferral = edition.SupportsUpdateDeferral;
        PauseDays = 7;
    }

    public ObservableCollection<DriverDisplay> Drivers { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    /// <summary>What the last search found, or that Windows Update could not be asked.</summary>
    [ObservableProperty]
    public partial string? SearchSummary { get; set; }

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial string? PausedUntilDisplay { get; set; }

    [ObservableProperty]
    public partial int PauseDays { get; set; }

    [ObservableProperty]
    public partial int FeatureDeferralDays { get; set; }

    [ObservableProperty]
    public partial int QualityDeferralDays { get; set; }

    /// <summary>False on Home, where the deferral policies do nothing.</summary>
    public bool SupportsDeferral { get; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;

        try
        {
            var drivers = await _scanner.ScanAsync(cancellationToken);

            Drivers.Clear();
            foreach (var driver in drivers)
            {
                Drivers.Add(new DriverDisplay(driver, DescribeVersion(driver), _localization));
            }

            IsEmpty = Drivers.Count == 0;

            // Read rather than asked for: showing how updates are scheduled should not cost an
            // administrator prompt. Only changing it does.
            ReadUpdateState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading the driver list failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Asks Windows Update which drivers have something newer. Nothing is installed here — the
    /// answer is a label on each row and a button that opens Windows Update.
    /// </summary>
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (IsSearching)
        {
            return;
        }

        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            StatusMessage = _localization.GetString("CommonElevationDeclined");
            return;
        }

        IsSearching = true;

        try
        {
            var response = await _elevation.SendAsync(new SearchDriverUpdatesRequest(), cancellationToken);

            if (response is not DriverUpdateSearchResponse { Succeeded: true } search)
            {
                // Offline, service disabled, managed network — all the same answer to the page.
                SearchSummary = _localization.GetString("DriversSearchFailed");
                return;
            }

            foreach (var display in Drivers)
            {
                display.HasUpdate = DriverCatalog.HasUpdate(display.Driver, search.Updates);
            }

            SearchSummary = _localization.GetString("DriversSearchedFormat", search.Updates.Count);
            StatusMessage = null;
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Pauses Windows Update for the chosen number of days.</summary>
    public Task PauseUpdatesAsync(CancellationToken cancellationToken)
    {
        (_, string expiry) = WindowsUpdatePolicy.PauseWindow(_timeProvider.GetUtcNow(), PauseDays);

        return ChangeUpdateScheduleAsync(
            new SetUpdatePauseRequest(PauseDays),
            new SystemSettingChangedEntry(UpdateSettingIds.Pause, DescribeCurrentPause(), expiry),
            cancellationToken);
    }

    /// <summary>Ends a pause now rather than waiting for it to expire.</summary>
    public Task ResumeUpdatesAsync(CancellationToken cancellationToken) =>
        ChangeUpdateScheduleAsync(
            new SetUpdatePauseRequest(0),
            new SystemSettingChangedEntry(UpdateSettingIds.Pause, DescribeCurrentPause(), string.Empty),
            cancellationToken);

    /// <summary>Applies both deferral periods. Only reachable where the policy actually applies.</summary>
    public Task ApplyDeferralAsync(CancellationToken cancellationToken) =>
        ChangeUpdateScheduleAsync(
            new SetUpdateDeferralRequest(FeatureDeferralDays, QualityDeferralDays),
            new SystemSettingChangedEntry(
                UpdateSettingIds.Deferral,
                WindowsUpdatePolicy.FormatDeferral(_current.DeferFeatureUpdatesDays, _current.DeferQualityUpdatesDays),
                WindowsUpdatePolicy.FormatDeferral(
                    WindowsUpdatePolicy.ClampFeatureDeferralDays(FeatureDeferralDays),
                    WindowsUpdatePolicy.ClampQualityDeferralDays(QualityDeferralDays))),
            cancellationToken);

    /// <summary>
    /// Closes the session so History shows one finished session for this visit. A session left
    /// open reads as one that was cut short, which is a different thing entirely.
    /// </summary>
    public async Task EndSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        await _journal.SaveAsync(_session with { CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);
        _session = null;
    }

    [RelayCommand]
    public void OpenWindowsUpdate() => Open(WindowsUiLauncher.WindowsUpdate);

    [RelayCommand]
    public void OpenDeviceManager() => Open(WindowsUiLauncher.DeviceManager);

    /// <summary>
    /// Sends one schedule change, with a journal entry written before it and stamped after.
    /// <para>
    /// Both of these are reversible machine-wide writes, so History has to be able to put them
    /// back (ADR-0006). The entry records the schedule as it stood, not as it was asked to become.
    /// </para>
    /// </summary>
    private async Task ChangeUpdateScheduleAsync(
        HelperRequest request,
        SystemSettingChangedEntry entry,
        CancellationToken cancellationToken)
    {
        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            StatusMessage = _localization.GetString("CommonElevationDeclined");
            return;
        }

        // Written before the change, so a session cut short by a crash is still revertable.
        _session ??= await _journal.StartAsync(SessionKind.Drivers, cancellationToken);
        _session = await _journal.AppendAsync(_session, entry, cancellationToken);
        int entryIndex = _session.Entries.Count - 1;

        var response = await _elevation.SendAsync(request, cancellationToken);

        _session = await _journal.UpdateEntryAsync(
            _session,
            entryIndex,
            entry with
            {
                Completed = response is WindowsUpdateStateResponse { Success: true },
                FailureDetail = (response as WindowsUpdateStateResponse)?.FailureDetail,
            },
            cancellationToken);

        if (response is not WindowsUpdateStateResponse { Success: true } state)
        {
            StatusMessage = _localization.GetString("UpdatesChangeRefused");
            return;
        }

        // Taken from what the helper read back, not from what was asked for: Windows clamps these.
        ApplyState(state.State);
        StatusMessage = null;
    }

    /// <summary>
    /// The pause as a journal entry records it — the instant it is due to end, or empty when
    /// updates are not paused at all.
    /// </summary>
    private string DescribeCurrentPause() => _current.PausedUntil is DateTimeOffset until
        ? WindowsUpdatePolicy.FormatTime(until)
        : string.Empty;

    private void ReadUpdateState()
    {
        var expiry = WindowsUpdatePolicy.ParseTime(_registry.GetStringValue(
            RegistryHiveName.LocalMachine,
            WindowsUpdatePolicy.PauseKeyPath,
            WindowsUpdatePolicy.PauseExpiryValueName));

        ApplyState(new WindowsUpdateState(
            WindowsUpdatePolicy.IsPauseActive(expiry, _timeProvider.GetUtcNow()) ? expiry : null,
            _registry.GetInt32Value(RegistryHiveName.LocalMachine, WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName) ?? 0,
            _registry.GetInt32Value(RegistryHiveName.LocalMachine, WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.QualityDeferValueName) ?? 0));
    }

    private void ApplyState(WindowsUpdateState state)
    {
        _current = state;
        IsPaused = state.IsPaused;
        PausedUntilDisplay = state.PausedUntil is DateTimeOffset until
            ? _localization.GetString("UpdatesPausedUntilFormat", until.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : null;

        FeatureDeferralDays = state.DeferFeatureUpdatesDays;
        QualityDeferralDays = state.DeferQualityUpdatesDays;
    }

    private static string DescribeVersion(DriverInfo driver) => driver.Date is DateTimeOffset date
        ? $"{driver.Version} · {date.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}"
        : driver.Version;

    private void Open(string target)
    {
        if (!_launcher.Open(target))
        {
            StatusMessage = _localization.GetString("SettingsCouldNotOpen");
        }
    }
}
