using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.ViewModels;

/// <summary>
/// The Startup tab: what runs when Windows starts, a switch for each one, and how long Windows
/// measured each one taking.
/// <para>
/// Turning something off writes the same value Task Manager writes rather than deleting the entry,
/// so it can always be turned back on — and every change is journaled before it happens, so
/// History can revert the lot (ADR-0006).
/// </para>
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupItemScanner _scanner;
    private readonly IStartupItemToggler _toggler;
    private readonly ISessionJournal _journal;
    private readonly IBootPerformanceSource _bootPerformance;
    private readonly ILaunchHistory _launchHistory;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    private SessionManifest? _session;
    private IReadOnlyList<StartupItem> _scanned = [];

    public StartupViewModel(
        IStartupItemScanner scanner,
        IStartupItemToggler toggler,
        ISessionJournal journal,
        IBootPerformanceSource bootPerformance,
        ILaunchHistory launchHistory,
        ILocalizationService localization,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        _scanner = scanner;
        _toggler = toggler;
        _journal = journal;
        _bootPerformance = bootPerformance;
        _launchHistory = launchHistory;
        _localization = localization;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ObservableCollection<StartupItemDisplay> Items { get; } = [];

    /// <summary>The preview of turning off what's suggested; filled by <see cref="PreparePlan"/>.</summary>
    public ObservableCollection<StartupPlanItem> PlanItems { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>False until Windows' timings have been read this run — the banner then offers to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsMeasurement))]
    public partial bool IsMeasured { get; set; }

    public bool NeedsMeasurement => !IsMeasured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    public partial int SuggestedCount { get; set; }

    public bool HasSuggestions => SuggestedCount > 0;

    [ObservableProperty]
    public partial string SuggestionHeadline { get; set; } = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = null;

        try
        {
            _scanned = await _scanner.ScanAsync(cancellationToken);

            // Never prompts: if the helper is already running the timings come for free, and if
            // not, the banner asks rather than a UAC prompt appearing because a tab was opened.
            await _bootPerformance.ReadAsync(allowPrompt: false, cancellationToken);
            Rebuild();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading startup items failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reads Windows' startup timings, asking for administrator approval to do so.</summary>
    public async Task MeasureAsync(CancellationToken cancellationToken)
    {
        var status = await _bootPerformance.ReadAsync(allowPrompt: true, cancellationToken);

        StatusMessage = status switch
        {
            BootPerformanceStatus.Declined => _localization.GetString("CommonElevationDeclined"),
            BootPerformanceStatus.Failed => _localization.GetString("SpeedCheckReadFailed"),
            _ => null,
        };

        if (status == BootPerformanceStatus.Read)
        {
            Rebuild();
        }
    }

    /// <summary>Fills the preview with what's suggested, every line ticked.</summary>
    public void PreparePlan()
    {
        PlanItems.Clear();
        foreach (var display in Items.Where(display => display.IsSuggested && display.IsEnabled))
        {
            PlanItems.Add(new StartupPlanItem(display, _localization.GetString("StartupPlanLineFormat", display.TimingDisplay, display.LastStartedDisplay)));
        }
    }

    /// <summary>
    /// Turns off what the person left ticked in the preview — each one journaled first, exactly as
    /// if they had flipped its switch.
    /// </summary>
    public async Task ApplyPlanAsync(CancellationToken cancellationToken)
    {
        foreach (var line in PlanItems.Where(line => line.IsSelected).ToList())
        {
            // Changed first and the switch moved after, so the switch's own Toggled event finds
            // nothing left to do rather than applying the same change a second time.
            await ChangeAsync(line.Display, enabled: false, cancellationToken);
        }

        PlanItems.Clear();
        RefreshSuggestions();
    }

    /// <summary>
    /// Applies a switch the user just flipped. If Windows refuses, the switch goes back rather
    /// than sitting in a state that never took effect.
    /// </summary>
    public async Task ApplyAsync(StartupItemDisplay display, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(display);

        // The switch moving to where Windows already is: Upkeep moved it, not the person.
        if (display.IsEnabled == display.AppliedEnabled)
        {
            return;
        }

        await ChangeAsync(display, display.IsEnabled, cancellationToken);
    }

    private async Task ChangeAsync(StartupItemDisplay display, bool enabled, CancellationToken cancellationToken)
    {
        try
        {
            _session ??= await _journal.StartAsync(SessionKind.Startup, cancellationToken);

            (var session, bool success) = await _toggler.SetEnabledAsync(_session, display.Item, enabled, cancellationToken);
            _session = session;

            if (!success)
            {
                RevertSwitch(display);
                StatusMessage = _localization.GetString("StartupChangeRefusedFormat", display.Name);
                return;
            }

            display.AppliedEnabled = enabled;
            display.IsEnabled = enabled;
            display.IsStale &= enabled;
            StatusMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Changing the startup state of {display.Name} failed.", ex, cancellationToken);
            RevertSwitch(display);
            StatusMessage = ex.Message;
        }
    }

    /// <summary>Closes the session so History shows it as finished. Called when the page goes away.</summary>
    public async Task EndSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        await _journal.SaveAsync(_session with { CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);
        _session = null;
    }

    /// <summary>Puts the switch back where it was, after Windows refused the change.</summary>
    private static void RevertSwitch(StartupItemDisplay display) => display.IsEnabled = display.AppliedEnabled;

    /// <summary>Ranks what the scan found by what Windows measured, slowest first.</summary>
    private void Rebuild()
    {
        var performance = _bootPerformance.Cached;
        IsMeasured = performance is not null;

        var insights = StartupAdvisor.Analyse(_scanned, performance, _launchHistory, _timeProvider.GetUtcNow());
        double slowest = insights.Max(insight => insight.Timing?.Average.TotalSeconds) ?? 0;

        Items.Clear();
        foreach (var insight in insights)
        {
            Items.Add(BuildDisplay(insight, slowest));
        }

        IsEmpty = Items.Count == 0;
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        SuggestedCount = Items.Count(display => display.IsSuggested && display.IsEnabled);
        SuggestionHeadline = _localization.GetString("StartupSuggestionHeadlineFormat", SuggestedCount);
    }

    private StartupItemDisplay BuildDisplay(StartupInsight insight, double slowestSeconds)
    {
        var item = insight.Item;
        string? disabledAt = item.DisabledAt is DateTimeOffset when
            ? _localization.GetString("StartupDisabledAtFormat", when.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : null;

        var display = new StartupItemDisplay(item, _localization.GetString($"StartupSource{item.Source}"), disabledAt)
        {
            IsSuggested = insight.Suggested,
            LastStartedDisplay = LastStartedText(insight.LastStartedByUser),
            // Only worth flagging on something still starting with Windows.
            IsStale = item.IsEnabled
                && (insight.LastStartedByUser is null
                    || _timeProvider.GetUtcNow() - insight.LastStartedByUser.Value > StartupAdvisor.UnusedAfter),
        };

        if (!IsMeasured)
        {
            display.TimingDisplay = "—";
            display.TimingTooltip = _localization.GetString("StartupTimingNotMeasuredTooltip");
        }
        else if (insight.Timing is StartupTiming timing)
        {
            display.TimingDisplay = Seconds(timing.Average);
            display.TimingTooltip = _localization.GetString("StartupTimingTooltipFormat", timing.Samples);
            display.TimingBarWidth = slowestSeconds > 0
                ? StartupItemDisplay.TimingBarMaxWidth * timing.Average.TotalSeconds / slowestSeconds
                : 0;
        }
        else
        {
            display.TimingDisplay = _localization.GetString("StartupNotTimed");
            display.TimingTooltip = _localization.GetString("StartupNotTimedTooltip");
        }

        return display;
    }

    private string Seconds(TimeSpan value) =>
        _localization.GetString("CommonSecondsFormat", value.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture));

    private string LastStartedText(DateTimeOffset? lastStarted)
    {
        if (lastStarted is not DateTimeOffset when)
        {
            return _localization.GetString("StartupLastStartedNever");
        }

        int days = (int)(_timeProvider.GetLocalNow().Date - TimeZoneInfo.ConvertTime(when, _timeProvider.LocalTimeZone).Date).TotalDays;
        return days switch
        {
            <= 0 => _localization.GetString("StartupLastStartedToday"),
            1 => _localization.GetString("StartupLastStartedYesterday"),
            _ => _localization.GetString("StartupLastStartedDaysAgoFormat", days),
        };
    }
}
