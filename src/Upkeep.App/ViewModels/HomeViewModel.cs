using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Storage;

namespace Upkeep.App.ViewModels;

/// <summary>
/// Home: where the disk stands, whether a cleanup is overdue, and what Upkeep did recently.
/// <para>
/// An earlier version of this class deliberately had none of that — no health status, no nudge —
/// on the reasoning that Home should be a starting point, not a dashboard. The status below is the
/// reversal of that call, made once there was a real design to react to rather than a hypothetical
/// one. It stays honest about a real trade-off: only signals the shell already knows without elevating
/// — the last *completed* cleanup session and today's drive usage — ever back it, never a live
/// rescan and never anything that would need the helper, so opening Home never carries the cost or
/// the prompt either of those would.
/// </para>
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    /// <summary>A cleanup older than this reads as overdue rather than recent.</summary>
    private const int StaleCleanupDays = 14;

    /// <summary>A drive at or beyond this usage is worth a nudge, not just a number in a list.</summary>
    private const double LowDiskThreshold = 0.90;

    private readonly IDriveScanner _driveScanner;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly ISessionJournal _journal;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty]
    public partial string GreetingDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial string UpdateBannerMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>True once a completed cleanup exists and it's recent enough not to need attention.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthAttention))]
    public partial bool IsHealthGood { get; set; } = true;

    /// <summary>The XAML-friendly inverse of <see cref="IsHealthGood"/> — two Borders switch on
    /// this pair rather than one Border switching brushes through a converter written for it alone.</summary>
    public bool IsHealthAttention => !IsHealthGood;

    [ObservableProperty]
    public partial string HealthHeadline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HealthDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNextAction { get; set; }

    [ObservableProperty]
    public partial string NextActionTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRecentActivity))]
    public partial bool HasLoadedActivity { get; set; }

    public ObservableCollection<DriveDisplay> Drives { get; } = [];

    public ObservableCollection<HistorySessionDisplay> RecentActivity { get; } = [];

    /// <summary>The empty-state message is only honest once a load has actually happened.</summary>
    public bool HasNoRecentActivity => HasLoadedActivity && RecentActivity.Count == 0;

    private string? _releaseUrl;

    public HomeViewModel(
        IDriveScanner driveScanner,
        IUpdateCheckService updateCheckService,
        ISessionJournal journal,
        ILocalizationService localization,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        _driveScanner = driveScanner;
        _updateCheckService = updateCheckService;
        _journal = journal;
        _localization = localization;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        GreetingDisplay = _localization.GetString(GreetingKeyFor(DateTime.Now.Hour));
    }

    /// <summary>Which greeting applies, by local hour. Separated out so it is testable without a clock.</summary>
    public static string GreetingKeyFor(int hour) => hour switch
    {
        >= 5 and < 12 => "HomeGreetingMorning",
        >= 12 and < 18 => "HomeGreetingAfternoon",
        >= 18 and < 23 => "HomeGreetingEvening",
        _ => "HomeGreetingNight",
    };

    /// <summary>A cleanup completed more than <see cref="StaleCleanupDays"/> days ago is overdue.</summary>
    public static bool IsCleanupStale(DateTimeOffset lastCompletedCleanupAt, DateTimeOffset now) =>
        (now - lastCompletedCleanupAt).TotalDays > StaleCleanupDays;

    /// <summary>A drive at or beyond <see cref="LowDiskThreshold"/> usage is worth surfacing.</summary>
    public static bool IsDriveLow(double usedFraction) => usedFraction >= LowDiskThreshold;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            LoadDrives();
            await LoadFromJournalAsync(cancellationToken);
            await CheckForUpdateAsync(cancellationToken);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadDrives()
    {
        Drives.Clear();
        DriveSnapshot? worstLowDrive = null;

        foreach (var drive in _driveScanner.GetFixedDrives())
        {
            string summary = _localization.GetString(
                "DriveFreeOfTotalFormat",
                ByteSize.Format(drive.FreeBytes),
                ByteSize.Format(drive.TotalBytes));

            Drives.Add(new DriveDisplay(drive.Name, summary, drive.UsedFraction));

            if (IsDriveLow(drive.UsedFraction) && (worstLowDrive is null || drive.UsedFraction > worstLowDrive.UsedFraction))
            {
                worstLowDrive = drive;
            }
        }

        if (worstLowDrive is not null)
        {
            HasNextAction = true;
            NextActionTitle = _localization.GetString(
                "HomeNextActionLowDiskTitleFormat",
                worstLowDrive.Name,
                (int)Math.Round(worstLowDrive.UsedFraction * 100));
        }
        else
        {
            HasNextAction = false;
        }
    }

    /// <summary>
    /// Reads the journal already on disk — no rescan, so this costs nothing a page load doesn't
    /// already pay elsewhere in the app. One read backs both the status and the activity list:
    /// they were a read each, which walked every session manifest twice on every visit to Home.
    /// </summary>
    private async Task LoadFromJournalAsync(CancellationToken cancellationToken)
    {
        RecentActivity.Clear();

        // Reset, so a reload that finds sessions clears an empty-state message left by one that
        // didn't: setting this to true when it is already true raises no change notification.
        HasLoadedActivity = false;

        try
        {
            var sessions = await _journal.ListAsync(cancellationToken);

            EvaluateHealth(sessions);

            // Newest first already, per the journal's own contract — Home just takes the front of it.
            foreach (var session in sessions.Take(3))
            {
                RecentActivity.Add(HistoryViewModel.BuildDisplay(session, _localization));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The journal is unreadable for some reason a scan didn't cause; Home says nothing
            // rather than guessing at a status it can't actually back up.
            await _logger.LogWarningAsync($"Home could not read session history: {ex.Message}", cancellationToken);
            IsHealthGood = true;
            HealthHeadline = string.Empty;
            HealthDetail = string.Empty;
        }
        finally
        {
            HasLoadedActivity = true;
        }
    }

    /// <summary>The last *completed* cleanup decides the status — an interrupted one says nothing
    /// about whether this machine has actually been cleaned.</summary>
    private void EvaluateHealth(IReadOnlyList<SessionManifest> sessions)
    {
        var lastCleanup = sessions.FirstOrDefault(session => session.Kind == SessionKind.Cleanup && session.CompletedAt is not null);

        if (lastCleanup is null)
        {
            IsHealthGood = false;
            HealthHeadline = _localization.GetString("HomeHealthAttentionHeadline");
            HealthDetail = _localization.GetString("HomeHealthNeverCleanedDetail");
            return;
        }

        var completedAt = lastCleanup.CompletedAt!.Value;
        string whenDisplay = completedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);

        if (IsCleanupStale(completedAt, _timeProvider.GetUtcNow()))
        {
            IsHealthGood = false;
            HealthHeadline = _localization.GetString("HomeHealthAttentionHeadline");
            HealthDetail = _localization.GetString("HomeHealthStaleFormat", whenDisplay);
            return;
        }

        IsHealthGood = true;
        HealthHeadline = _localization.GetString("HomeHealthGoodHeadline");
        HealthDetail = lastCleanup.FreedBytes > 0
            ? _localization.GetString("HomeHealthGoodWithFreedFormat", whenDisplay, ByteSize.Format(lastCleanup.FreedBytes))
            : _localization.GetString("HomeHealthGoodFormat", whenDisplay);
    }

    private async Task CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _updateCheckService.CheckForUpdateAsync(cancellationToken);
            HasUpdateAvailable = result.IsUpdateAvailable;
            _releaseUrl = result.ReleaseUrl;

            if (result.IsUpdateAvailable && result.LatestVersion is not null)
            {
                UpdateBannerMessage = _localization.GetString("HomeUpdateAvailableFormat", result.LatestVersion);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The check is a convenience; a failure never blocks Home (ADR-0004).
            await _logger.LogWarningAsync($"Home could not check for updates: {ex.Message}", cancellationToken);
        }
    }

    [RelayCommand]
    private async Task OpenReleaseAsync()
    {
        if (_releaseUrl is null)
        {
            return;
        }

        await Windows.System.Launcher.LaunchUriAsync(new Uri(_releaseUrl));
    }
}
