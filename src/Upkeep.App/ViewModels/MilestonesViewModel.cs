using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Milestones;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.ViewModels;

/// <summary>One month on the rhythm row.</summary>
public sealed record MonthDisplay(string Label, string Description, bool Done, bool IsCurrent)
{
    /// <summary>This month, with nothing done yet: open, not missed.</summary>
    public bool IsOpen => IsCurrent && !Done;

    public bool IsMissed => !Done && !IsCurrent;
}

/// <summary>One milestone card.</summary>
public sealed record MilestoneDisplay(string Title, string Detail, bool Achieved, double ProgressPercent, bool ShowsProgress)
{
    public bool IsLocked => !Achieved;
}

/// <summary>
/// The Milestones section: how regularly the PC is looked after, what that has added up to, and
/// milestones drawn from Windows' own measurements.
/// <para>
/// Read-only and derived — everything comes from the session journal and Speed check's timings,
/// so there's no score to game and nothing to keep in sync (see <see cref="MilestoneEvaluator"/>).
/// It never prompts for anything, never nags, and never counts days.
/// </para>
/// </summary>
public sealed partial class MilestonesViewModel : ObservableObject
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        [MilestoneEvaluator.FirstTuneUp] = "MilestoneFirstTuneUp",
        [MilestoneEvaluator.TenGigabytes] = "MilestoneTenGigabytes",
        [MilestoneEvaluator.SpeedTuneUp] = "MilestoneSpeedTuneUp",
        [MilestoneEvaluator.UnderFortySeconds] = "MilestoneUnderFortySeconds",
        [MilestoneEvaluator.TenSecondsFaster] = "MilestoneTenSecondsFaster",
        [MilestoneEvaluator.LeanStartup] = "MilestoneLeanStartup",
        [MilestoneEvaluator.ThreeMonths] = "MilestoneThreeMonths",
        [MilestoneEvaluator.SixMonths] = "MilestoneSixMonths",
    };

    private readonly ISessionJournal _journal;
    private readonly IStartupItemScanner _startupScanner;
    private readonly IBootPerformanceSource _bootPerformance;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public MilestonesViewModel(
        ISessionJournal journal,
        IStartupItemScanner startupScanner,
        IBootPerformanceSource bootPerformance,
        ILocalizationService localization,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        _journal = journal;
        _startupScanner = startupScanner;
        _bootPerformance = bootPerformance;
        _localization = localization;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ObservableCollection<MonthDisplay> Months { get; } = [];

    public ObservableCollection<MilestoneDisplay> Milestones { get; } = [];

    [ObservableProperty]
    public partial string StreakHeadline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RhythmNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FreedDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangesDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StartupSavedDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StartupSavedCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AchievedSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sessions = await _journal.ListAsync(cancellationToken);
            var startupItems = await _startupScanner.ScanAsync(cancellationToken);

            // Never prompts: startup milestones simply wait until Speed check has read the timings.
            await _bootPerformance.ReadAsync(allowPrompt: false, cancellationToken);

            var progress = MilestoneEvaluator.Evaluate(
                sessions,
                _bootPerformance.Cached,
                startupItems.Count(item => item.IsEnabled && item.CanToggle),
                _timeProvider.GetUtcNow(),
                _timeProvider.LocalTimeZone);

            Show(progress);
            StatusMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Milestones failed to load.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
    }

    private void Show(MilestoneProgress progress)
    {
        var rhythm = progress.Rhythm;
        StreakHeadline = _localization.GetString("MilestonesStreakFormat", rhythm.Streak);
        RhythmNote = _localization.GetString(rhythm.DoneThisMonth ? "MilestonesRhythmDone" : "MilestonesRhythmOpen");

        Months.Clear();
        foreach (var month in rhythm.Months)
        {
            string label = new DateTime(month.Year, month.Month, 1).ToString("MMM", CultureInfo.CurrentCulture);
            string state = month.Done ? "MilestonesMonthDone" : month.IsCurrent ? "MilestonesMonthOpen" : "MilestonesMonthMissed";
            Months.Add(new MonthDisplay(label, _localization.GetString(state, label), month.Done, month.IsCurrent));
        }

        FreedDisplay = ByteSize.Format(progress.FreedBytes);
        ChangesDisplay = progress.Changes.ToString(CultureInfo.CurrentCulture);

        if (progress.StartupSaved is TimeSpan saved && saved > TimeSpan.Zero)
        {
            StartupSavedDisplay = _localization.GetString("CommonSecondsFormat", saved.TotalSeconds.ToString("0", CultureInfo.CurrentCulture));
            StartupSavedCaption = _localization.GetString("MilestonesStartupSavedCaption");
        }
        else
        {
            StartupSavedDisplay = "—";
            StartupSavedCaption = _localization.GetString("MilestonesStartupSavedUnknown");
        }

        Milestones.Clear();
        foreach (var milestone in progress.Milestones)
        {
            string key = Keys[milestone.Id];
            Milestones.Add(new MilestoneDisplay(
                _localization.GetString(key + "Title"),
                _localization.GetString(milestone.Progress is null ? "MilestoneNeedsSpeedCheck" : key + "Detail"),
                milestone.Achieved,
                (milestone.Progress ?? 0) * 100,
                !milestone.Achieved && milestone.Progress is not null));
        }

        AchievedSummary = _localization.GetString(
            "MilestonesAchievedFormat",
            progress.Milestones.Count(milestone => milestone.Achieved),
            progress.Milestones.Count);
    }
}
