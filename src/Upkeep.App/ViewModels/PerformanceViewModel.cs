using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.ViewModels;

/// <summary>
/// The Performance tab: the handful of Windows settings with a single obvious meaning, plus links
/// to the Windows dialogs that own everything else.
/// <para>
/// Each change is journaled with the value it replaced, so History can put it back. The list is
/// deliberately short — Upkeep does not reimplement a control panel it can simply open.
/// </para>
/// </summary>
public sealed partial class PerformanceViewModel : ObservableObject
{
    /// <summary>Windows Search. Indexing is this service, so the switch is a start-type change.</summary>
    private const string SearchServiceName = "WSearch";

    private readonly IPerformanceSettings _settings;
    private readonly IPowerModeSettings _powerMode;
    private readonly IBackgroundAppSettings _backgroundApps;
    private readonly IServiceScanner _scanner;
    private readonly IElevationService _elevation;
    private readonly IWindowsUiLauncher _launcher;
    private readonly ISessionJournal _journal;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    private SessionManifest? _session;
    private bool _loading;
    private PowerModeState? _powerModeState;

    public PerformanceViewModel(
        IPerformanceSettings settings,
        IPowerModeSettings powerMode,
        IBackgroundAppSettings backgroundApps,
        IServiceScanner scanner,
        IElevationService elevation,
        IWindowsUiLauncher launcher,
        ISessionJournal journal,
        ILocalizationService localization,
        IAppLogger logger)
    {
        _settings = settings;
        _powerMode = powerMode;
        _backgroundApps = backgroundApps;
        _scanner = scanner;
        _elevation = elevation;
        _launcher = launcher;
        _journal = journal;
        _localization = localization;
        _logger = logger;
    }

    public ObservableCollection<PowerPlan> PowerPlans { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool AnimationsEnabled { get; set; }

    [ObservableProperty]
    public partial bool TransparencyEnabled { get; set; }

    [ObservableProperty]
    public partial bool IndexingEnabled { get; set; }

    [ObservableProperty]
    public partial PowerPlan? SelectedPowerPlan { get; set; }

    /// <summary>False on PCs that only expose Balanced, where the page points at Settings instead.</summary>
    [ObservableProperty]
    public partial bool HasPowerPlanChoice { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Store apps and whether each may run in the background.</summary>
    public ObservableCollection<BackgroundAppDisplay> BackgroundApps { get; } = [];

    /// <summary>False when Windows wouldn't report a power mode Upkeep knows; the section hides.</summary>
    [ObservableProperty]
    public partial bool HasPowerMode { get; set; }

    /// <summary>Power mode applies only under the Balanced plan; otherwise the pickers would do nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerModeIsInert))]
    public partial bool PowerModeApplies { get; set; }

    public bool PowerModeIsInert => !PowerModeApplies;

    /// <summary>Desktops have no battery, so only the plugged-in mode is shown.</summary>
    [ObservableProperty]
    public partial bool HasBattery { get; set; }

    /// <summary>Index into the picker, which lists the modes in <see cref="PowerMode"/> order.</summary>
    [ObservableProperty]
    public partial int PluggedInModeIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int OnBatteryModeIndex { get; set; } = -1;

    [ObservableProperty]
    public partial bool HasBackgroundApps { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Set through the guard so reading the current state doesn't look like the user changing it.
        _loading = true;

        try
        {
            var state = _settings.Read();

            AnimationsEnabled = state.AnimationsEnabled;
            TransparencyEnabled = state.TransparencyEnabled;

            PowerPlans.Clear();
            foreach (var plan in state.PowerPlans)
            {
                PowerPlans.Add(plan);
            }

            SelectedPowerPlan = state.PowerPlans.FirstOrDefault(plan => plan.IsActive);
            HasPowerPlanChoice = state.PowerPlans.Count > 1;

            IndexingEnabled = await ReadIndexingEnabledAsync(cancellationToken);

            ShowPowerMode(_powerMode.Read());

            BackgroundApps.Clear();
            foreach (var app in _backgroundApps.Read())
            {
                BackgroundApps.Add(new BackgroundAppDisplay(app, AccessLabel(app.Access)));
            }

            HasBackgroundApps = BackgroundApps.Count > 0;
            StatusMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading the performance settings failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Closes the session so History shows one finished session for this visit.</summary>
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
    public void OpenAdvancedPerformanceOptions() => Open(WindowsUiLauncher.PerformanceOptions);

    [RelayCommand]
    public void OpenPowerSettings() => Open(WindowsUiLauncher.PowerSettings);

    [RelayCommand]
    public void OpenIndexingOptions() =>
        Open(WindowsUiLauncher.IndexingOptionsCommand, WindowsUiLauncher.IndexingOptionsArguments);

    /// <summary>Applies the animations switch, putting it back if Windows would not take it.</summary>
    public Task ApplyAnimationsAsync(CancellationToken cancellationToken) =>
        ApplySettingAsync(
            PerformanceSettingIds.Animations,
            AnimationsEnabled,
            enabled => _settings.SetAnimationsEnabled(enabled, out string? failure) ? null : failure ?? string.Empty,
            () => AnimationsEnabled = !AnimationsEnabled,
            cancellationToken);

    /// <summary>Applies the transparency switch, putting it back if Windows would not take it.</summary>
    public Task ApplyTransparencyAsync(CancellationToken cancellationToken) =>
        ApplySettingAsync(
            PerformanceSettingIds.Transparency,
            TransparencyEnabled,
            enabled => _settings.SetTransparencyEnabled(enabled, out string? failure) ? null : failure ?? string.Empty,
            () => TransparencyEnabled = !TransparencyEnabled,
            cancellationToken);

    /// <summary>
    /// Applies the power plan the user picked. Windows owns the list, so this only ever activates
    /// a plan that was already on the machine.
    /// </summary>
    public async Task ApplyPowerPlanAsync(CancellationToken cancellationToken)
    {
        if (_loading || SelectedPowerPlan is not PowerPlan plan)
        {
            return;
        }

        var previous = PowerPlans.FirstOrDefault(candidate => candidate.IsActive);
        if (previous is not null && previous.Id == plan.Id)
        {
            return;
        }

        var entry = new SystemSettingChangedEntry(
            PerformanceSettingIds.PowerPlan,
            previous?.Id.ToString() ?? string.Empty,
            plan.Id.ToString());

        int entryIndex = await JournalAsync(entry, cancellationToken);

        bool succeeded = _settings.SetActivePowerPlan(plan.Id, out string? failure);
        await StampAsync(entryIndex, entry, succeeded, failure, cancellationToken);

        if (succeeded)
        {
            RefreshActivePlan(plan);
            StatusMessage = null;
            return;
        }

        await _logger.LogErrorAsync($"Windows would not activate the power plan {plan.Name}: {failure}", null, cancellationToken);
        StatusMessage = _localization.GetString("PerformanceChangeRefusedFormat", plan.Name);
    }

    /// <summary>
    /// Applies the indexing switch. Indexing is the Windows Search service, so this is a service
    /// start-type change and goes through the elevated helper like any other.
    /// </summary>
    public async Task ApplyIndexingAsync(CancellationToken cancellationToken)
    {
        if (_loading)
        {
            return;
        }

        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            IndexingEnabled = !IndexingEnabled;
            StatusMessage = _localization.GetString("CommonElevationDeclined");
            return;
        }

        var target = IndexingEnabled ? ServiceStartType.AutomaticDelayed : ServiceStartType.Disabled;
        var previous = IndexingEnabled ? ServiceStartType.Disabled : ServiceStartType.AutomaticDelayed;

        var entry = new ServiceStartTypeChangedEntry(SearchServiceName, previous.ToString(), target.ToString());
        int entryIndex = await JournalAsync(entry, cancellationToken);

        var response = await _elevation.SendAsync(new SetServiceStartTypeRequest(SearchServiceName, target), cancellationToken);
        bool succeeded = response is ServiceChangeResponse { Success: true };

        await StampAsync(entryIndex, entry, succeeded, (response as ServiceChangeResponse)?.Detail, cancellationToken);

        if (succeeded)
        {
            StatusMessage = null;
            return;
        }

        IndexingEnabled = !IndexingEnabled;
        StatusMessage = _localization.GetString("PerformanceChangeRefusedFormat", _localization.GetString("PerformanceIndexingName"));
    }

    /// <summary>Applies the power mode picked for one power source, putting the picker back if Windows refuses.</summary>
    public async Task ApplyPowerModeAsync(PowerSource source, CancellationToken cancellationToken)
    {
        if (_loading || _powerModeState is not PowerModeState state)
        {
            return;
        }

        int index = source == PowerSource.PluggedIn ? PluggedInModeIndex : OnBatteryModeIndex;
        PowerMode? previous = source == PowerSource.PluggedIn ? state.PluggedIn : state.OnBattery;

        if (index < 0 || !Enum.IsDefined((PowerMode)index) || (PowerMode)index == previous)
        {
            return;
        }

        var mode = (PowerMode)index;
        var entry = new SystemSettingChangedEntry(PowerModes.SettingId(source), previous?.ToString() ?? string.Empty, mode.ToString());
        int entryIndex = await JournalAsync(entry, cancellationToken);

        bool succeeded = _powerMode.SetMode(source, mode, out string? failure);
        await StampAsync(entryIndex, entry, succeeded, failure, cancellationToken);

        if (succeeded)
        {
            _powerModeState = source == PowerSource.PluggedIn ? state with { PluggedIn = mode } : state with { OnBattery = mode };
            StatusMessage = null;
            return;
        }

        await _logger.LogErrorAsync($"Windows would not set the power mode: {failure}", null, cancellationToken);
        ShowPowerMode(state);
        StatusMessage = _localization.GetString("PerformanceChangeRefusedFormat", _localization.GetString("PerformancePowerModeName"));
    }

    /// <summary>
    /// Applies one background-app switch. The app's values are journaled exactly as they were
    /// before anything is written, so History can put back "Always" as readily as "Power optimized".
    /// </summary>
    public async Task ApplyBackgroundAccessAsync(BackgroundAppDisplay display, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(display);

        // The switch moving to where Windows already is: Upkeep moved it, not the person.
        if (_loading || display.IsAllowed == display.AppliedAllowed)
        {
            return;
        }

        bool allowed = display.IsAllowed;
        var entry = new BackgroundAccessChangedEntry(display.FamilyName, display.Name, _backgroundApps.Snapshot(display.FamilyName), allowed);
        int entryIndex = await JournalAsync(entry, cancellationToken);

        bool succeeded = _backgroundApps.SetAllowed(display.FamilyName, allowed, out string? failure);
        await StampAsync(entryIndex, entry, succeeded, failure, cancellationToken);

        if (succeeded)
        {
            display.AppliedAllowed = allowed;
            display.AccessLabel = AccessLabel(allowed ? BackgroundAccess.PowerOptimized : BackgroundAccess.Never);
            StatusMessage = null;
            return;
        }

        await _logger.LogErrorAsync($"Windows would not change background access for {display.Name}: {failure}", null, cancellationToken);
        display.IsAllowed = display.AppliedAllowed;
        StatusMessage = _localization.GetString("PerformanceChangeRefusedFormat", display.Name);
    }

    private void ShowPowerMode(PowerModeState state)
    {
        bool wasLoading = _loading;
        _loading = true;

        _powerModeState = state;
        HasPowerMode = state.PluggedIn is not null;
        PowerModeApplies = state.AppliesToActivePlan;
        HasBattery = state.HasBattery && state.OnBattery is not null;
        PluggedInModeIndex = state.PluggedIn is PowerMode pluggedIn ? (int)pluggedIn : -1;
        OnBatteryModeIndex = state.OnBattery is PowerMode onBattery ? (int)onBattery : -1;

        _loading = wasLoading;
    }

    private string AccessLabel(BackgroundAccess access) => _localization.GetString($"PerformanceBackground{access}");

    private async Task ApplySettingAsync(
        string settingId,
        bool enabled,
        Func<bool, string?> apply,
        Action revert,
        CancellationToken cancellationToken)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            var entry = new SystemSettingChangedEntry(settingId, (!enabled).ToString(), enabled.ToString());
            int entryIndex = await JournalAsync(entry, cancellationToken);

            string? failure = apply(enabled);
            await StampAsync(entryIndex, entry, failure is null, failure, cancellationToken);

            if (failure is null)
            {
                StatusMessage = null;
                return;
            }

            await _logger.LogErrorAsync($"Windows would not change {settingId}: {failure}", null, cancellationToken);
            revert();
            StatusMessage = _localization.GetString("PerformanceChangeRefusedFormat", settingId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Changing {settingId} failed.", ex, cancellationToken);
            revert();
            StatusMessage = ex.Message;
        }
    }

    /// <summary>
    /// Writes the entry before the change and hands back where it landed, so how the change went
    /// can be stamped onto it afterwards (ADR-0006).
    /// </summary>
    private async Task<int> JournalAsync(SessionEntry entry, CancellationToken cancellationToken)
    {
        _session ??= await _journal.StartAsync(SessionKind.Startup, cancellationToken);
        _session = await _journal.AppendAsync(_session, entry, cancellationToken);
        return _session.Entries.Count - 1;
    }

    /// <summary>
    /// Records how the change actually went. A revert skips any entry that never completed, so
    /// this is what makes a setting revertable at all rather than only recorded.
    /// </summary>
    private async Task StampAsync(
        int entryIndex,
        SessionEntry entry,
        bool succeeded,
        string? failure,
        CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        _session = await _journal.UpdateEntryAsync(
            _session,
            entryIndex,
            entry with { Completed = succeeded, FailureDetail = failure },
            cancellationToken);
    }

    private async Task<bool> ReadIndexingEnabledAsync(CancellationToken cancellationToken)
    {
        var services = await _scanner.ScanAsync(cancellationToken);
        var search = services.FirstOrDefault(service => service.Name.Equals(SearchServiceName, StringComparison.OrdinalIgnoreCase));

        // A machine without Windows Search reads as off rather than failing the tab.
        return search is not null && !string.Equals(search.StartType, "Disabled", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshActivePlan(PowerPlan activated)
    {
        for (int index = 0; index < PowerPlans.Count; index++)
        {
            PowerPlans[index] = PowerPlans[index] with { IsActive = PowerPlans[index].Id == activated.Id };
        }

        SelectedPowerPlan = PowerPlans.FirstOrDefault(plan => plan.IsActive);
    }

    private void Open(string target, string? arguments = null)
    {
        if (!_launcher.Open(target, arguments))
        {
            StatusMessage = _localization.GetString("PerformanceCouldNotOpenWindows");
        }
    }
}
