using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.ViewModels;

/// <summary>One start on the Speed check chart.</summary>
public sealed record BootBar(double Height, string Tooltip, bool IsLatest)
{
    public bool IsEarlier => !IsLatest;
}

/// <summary>One thing that would help, and which tab does it.</summary>
/// <param name="Title">What Upkeep found.</param>
/// <param name="Detail">What changing it does — including when the effect is small.</param>
/// <param name="Badge">How much difference to expect, in words rather than an invented number.</param>
/// <param name="TargetTab">The tab on this page where the change is made.</param>
public sealed record SpeedRecommendation(string Title, string Detail, string Badge, string TargetTab);

/// <summary>
/// The Speed check tab: how long Windows takes to start, what would make it faster, and — once
/// Windows has timed a start since a change — how much faster it actually got.
/// <para>
/// Every number is one Windows recorded (ADR-0010). Recommendations name what Windows measured or
/// what a setting is; none of them promises a saving, because only the next start can show one.
/// </para>
/// </summary>
public sealed partial class SpeedCheckViewModel : ObservableObject
{
    /// <summary>The tallest bar on the chart, in pixels.</summary>
    public const double ChartHeight = 96;

    /// <summary>Within this, before and after are "about the same" rather than a win or a loss.</summary>
    private static readonly TimeSpan Noise = TimeSpan.FromSeconds(1);

    private readonly IBootPerformanceSource _bootPerformance;
    private readonly ISessionJournal _journal;
    private readonly IStartupItemScanner _startupScanner;
    private readonly ILaunchHistory _launchHistory;
    private readonly IPowerModeSettings _powerMode;
    private readonly IBackgroundAppSettings _backgroundApps;
    private readonly IPerformanceSettings _performance;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public SpeedCheckViewModel(
        IBootPerformanceSource bootPerformance,
        ISessionJournal journal,
        IStartupItemScanner startupScanner,
        ILaunchHistory launchHistory,
        IPowerModeSettings powerMode,
        IBackgroundAppSettings backgroundApps,
        IPerformanceSettings performance,
        ILocalizationService localization,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        _bootPerformance = bootPerformance;
        _journal = journal;
        _startupScanner = startupScanner;
        _launchHistory = launchHistory;
        _powerMode = powerMode;
        _backgroundApps = backgroundApps;
        _performance = performance;
        _localization = localization;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ObservableCollection<BootBar> Bars { get; } = [];

    public ObservableCollection<SpeedRecommendation> Recommendations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotMeasuring))]
    public partial bool IsMeasuring { get; set; }

    public bool IsNotMeasuring => !IsMeasuring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsMeasurement))]
    public partial bool IsMeasured { get; set; }

    public bool NeedsMeasurement => !IsMeasured;

    /// <summary>Measured, but Windows had no start on record — a fresh install, or a cleared log.</summary>
    [ObservableProperty]
    public partial bool HasNoBoots { get; set; }

    [ObservableProperty]
    public partial bool HasLastBoot { get; set; }

    [ObservableProperty]
    public partial string LastBootDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastBootCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChartStartLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChartEndLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasComparison { get; set; }

    [ObservableProperty]
    public partial string ComparisonHeadline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ComparisonDetail { get; set; } = string.Empty;

    /// <summary>A change is in place but Windows hasn't timed a start since.</summary>
    [ObservableProperty]
    public partial bool IsWaitingForRestart { get; set; }

    [ObservableProperty]
    public partial string WaitingForRestartMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRecommendations { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Never prompts on its own: the tab is often the first thing on the page, and a UAC prompt
        // for opening a page is not one anybody asked for.
        await RefreshAsync(allowPrompt: false, cancellationToken);
    }

    /// <summary>Reads Windows' startup timings, asking for administrator approval to do so.</summary>
    public Task MeasureAsync(CancellationToken cancellationToken) => RefreshAsync(allowPrompt: true, cancellationToken);

    private async Task RefreshAsync(bool allowPrompt, CancellationToken cancellationToken)
    {
        if (IsMeasuring)
        {
            return;
        }

        IsMeasuring = true;
        try
        {
            var status = await _bootPerformance.ReadAsync(allowPrompt, cancellationToken);
            StatusMessage = status switch
            {
                BootPerformanceStatus.Declined => _localization.GetString("CommonElevationDeclined"),
                BootPerformanceStatus.Failed => _localization.GetString("SpeedCheckReadFailed"),
                _ => null,
            };

            var performance = _bootPerformance.Cached;
            IsMeasured = performance is not null;

            if (performance is not null)
            {
                ShowBoots(performance.Boots);
                ShowComparison(performance.Boots, SpeedCheck.LastChange(await _journal.ListAsync(cancellationToken)));
            }

            await ShowRecommendationsAsync(performance, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Speed check failed to load.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    private void ShowBoots(IReadOnlyList<BootRecord> boots)
    {
        var recent = SpeedCheck.Recent(boots);
        HasNoBoots = recent.Count == 0;
        HasLastBoot = recent.Count > 0;

        Bars.Clear();
        if (recent.Count == 0)
        {
            return;
        }

        var latest = recent[0];
        LastBootDisplay = Seconds(latest.Duration, wholeSeconds: true);
        LastBootCaption = _localization.GetString(
            "SpeedCheckLastBootCaptionFormat",
            ShortDate(latest.At),
            recent.Count,
            Seconds(SpeedCheck.Average(recent) ?? latest.Duration, wholeSeconds: true));

        // Oldest on the left, as a timeline reads.
        var chronological = recent.Reverse().ToList();
        double slowest = chronological.Max(boot => boot.Duration.TotalSeconds);
        foreach (var boot in chronological)
        {
            Bars.Add(new BootBar(
                ChartHeight * boot.Duration.TotalSeconds / slowest,
                _localization.GetString("SpeedCheckBarTooltipFormat", ShortDate(boot.At), Seconds(boot.Duration, wholeSeconds: true)),
                boot == latest));
        }

        ChartStartLabel = ShortDate(chronological[0].At);
        ChartEndLabel = ShortDate(latest.At);
    }

    private void ShowComparison(IReadOnlyList<BootRecord> boots, SpeedChange? change)
    {
        HasComparison = false;
        IsWaitingForRestart = false;

        if (change is null)
        {
            return;
        }

        if (SpeedCheck.Compare(boots, change.At) is not SpeedComparison comparison)
        {
            // Changed, but there's nothing after it to compare yet — or nothing before it.
            IsWaitingForRestart = !boots.Any(boot => boot.At > change.At);
            WaitingForRestartMessage = _localization.GetString("SpeedCheckWaitingForRestartFormat", ShortDate(change.At));
            return;
        }

        HasComparison = true;
        TimeSpan saved = comparison.Saved;
        ComparisonHeadline = saved.Duration() < Noise
            ? _localization.GetString("SpeedCheckAboutTheSame")
            : saved > TimeSpan.Zero
                ? _localization.GetString("SpeedCheckFasterFormat", Seconds(saved, wholeSeconds: true))
                : _localization.GetString("SpeedCheckSlowerFormat", Seconds(saved.Negate(), wholeSeconds: true));

        string detail = _localization.GetString(
            "SpeedCheckComparisonDetailFormat",
            comparison.BeforeCount,
            Seconds(comparison.BeforeAverage, wholeSeconds: true),
            ShortDate(change.At),
            comparison.AfterCount,
            Seconds(comparison.AfterAverage, wholeSeconds: true));

        ComparisonDetail = comparison.IsSmallSample
            ? detail + " " + _localization.GetString("SpeedCheckSmallSample")
            : detail;
    }

    private async Task ShowRecommendationsAsync(BootPerformance? performance, CancellationToken cancellationToken)
    {
        Recommendations.Clear();

        if (performance is not null)
        {
            var items = await _startupScanner.ScanAsync(cancellationToken);
            var suggested = StartupAdvisor.Analyse(items, performance, _launchHistory, _timeProvider.GetUtcNow())
                .Where(insight => insight.Suggested)
                .ToList();

            if (suggested.Count > 0)
            {
                TimeSpan slowest = suggested.Max(insight => insight.Timing?.Average ?? TimeSpan.Zero);
                Recommendations.Add(new SpeedRecommendation(
                    _localization.GetString("SpeedCheckStartupTitleFormat", suggested.Count),
                    _localization.GetString("SpeedCheckStartupDetailFormat", Seconds(slowest, wholeSeconds: false)),
                    _localization.GetString("SpeedCheckBadgeEveryStart"),
                    "Startup"));
            }
        }

        if (_powerMode.Read().IsThrottledWhilePluggedIn)
        {
            Recommendations.Add(new SpeedRecommendation(
                _localization.GetString("SpeedCheckPowerModeTitle"),
                _localization.GetString("SpeedCheckPowerModeDetail"),
                _localization.GetString("SpeedCheckBadgeFeltEverywhere"),
                "Performance"));
        }

        if (_performance.Read().AnimationsEnabled)
        {
            Recommendations.Add(new SpeedRecommendation(
                _localization.GetString("SpeedCheckAnimationsTitle"),
                _localization.GetString("SpeedCheckAnimationsDetail"),
                _localization.GetString("SpeedCheckBadgeFeltEverywhere"),
                "Performance"));
        }

        int inBackground = _backgroundApps.Read().Count(app => app.Access != BackgroundAccess.Never);
        if (inBackground > 0)
        {
            Recommendations.Add(new SpeedRecommendation(
                _localization.GetString("SpeedCheckBackgroundTitleFormat", inBackground),
                _localization.GetString("SpeedCheckBackgroundDetail"),
                _localization.GetString("SpeedCheckBadgeSmall"),
                "Performance"));
        }

        HasRecommendations = Recommendations.Count > 0;
    }

    private string Seconds(TimeSpan value, bool wholeSeconds) =>
        _localization.GetString(
            "CommonSecondsFormat",
            value.TotalSeconds.ToString(wholeSeconds ? "0" : "0.0", CultureInfo.CurrentCulture));

    private string ShortDate(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, _timeProvider.LocalTimeZone).ToString("d MMM", CultureInfo.CurrentCulture);
}
