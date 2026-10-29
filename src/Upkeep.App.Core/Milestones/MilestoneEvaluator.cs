using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Milestones;

/// <summary>One calendar month, and whether a tune-up happened in it.</summary>
public sealed record MonthMark(int Year, int Month, bool Done, bool IsCurrent);

/// <summary>
/// How regularly the PC is looked after. Counted in months, never days: a monthly tune-up is
/// plenty, and a daily streak would reward clearing caches that are better left alone.
/// </summary>
/// <param name="Streak">Months in a row with a tune-up. The current month only breaks it once it's over.</param>
/// <param name="Months">The last six months, oldest first, the current one last.</param>
/// <param name="DoneThisMonth">Whether this month already has one — then nothing is due until next month.</param>
public sealed record Rhythm(int Streak, IReadOnlyList<MonthMark> Months, bool DoneThisMonth);

/// <summary>One milestone and how close it is.</summary>
/// <param name="Id">Stable identifier; the app turns it into a localized title.</param>
/// <param name="Achieved">Reached.</param>
/// <param name="Progress">0 to 1, or null when it can't be known yet — startup milestones before Speed check has read anything.</param>
public sealed record Milestone(string Id, bool Achieved, double? Progress);

/// <summary>Everything the Milestones tab shows.</summary>
/// <param name="FreedBytes">Space freed by sessions that weren't reverted.</param>
/// <param name="Changes">Completed changes still in place.</param>
/// <param name="StartupSaved">Before against after the latest speed change, when Windows has timed both.</param>
public sealed record MilestoneProgress(long FreedBytes, int Changes, TimeSpan? StartupSaved, Rhythm Rhythm, IReadOnlyList<Milestone> Milestones);

/// <summary>
/// Works out rhythm and milestones from what Upkeep already records — the session journal and
/// Windows' own startup timings. Nothing here is stored separately, so nothing can drift from
/// what actually happened, and reverting a session takes its credit with it.
/// <para>
/// Every milestone is an outcome Windows measured or a habit worth having. None rewards volume
/// for its own sake, so there is never a reason to clean what doesn't need cleaning.
/// </para>
/// </summary>
public static class MilestoneEvaluator
{
    public const string FirstTuneUp = "first-tune-up";
    public const string TenGigabytes = "ten-gigabytes";
    public const string SpeedTuneUp = "speed-tune-up";
    public const string UnderFortySeconds = "under-forty-seconds";
    public const string TenSecondsFaster = "ten-seconds-faster";
    public const string LeanStartup = "lean-startup";
    public const string ThreeMonths = "three-months";
    public const string SixMonths = "six-months";

    public const long TenGigabytesInBytes = 10L * 1024 * 1024 * 1024;
    public const int LeanStartupApps = 6;

    private const int MonthsShown = 6;

    private static readonly TimeSpan FortySeconds = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan TenSeconds = TimeSpan.FromSeconds(10);

    /// <param name="sessions">Every session in the journal.</param>
    /// <param name="performance">Windows' startup timings, or null if they haven't been read this run.</param>
    /// <param name="startupAppsOn">How many startup items are on, or null if unknown.</param>
    /// <param name="now">The current time.</param>
    /// <param name="timeZone">Months are the person's months, not UTC's.</param>
    public static MilestoneProgress Evaluate(
        IReadOnlyList<SessionManifest> sessions,
        BootPerformance? performance,
        int? startupAppsOn,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(timeZone);

        var kept = sessions.Where(session => session.RevertedAt is null && session.CompletedCount > 0).ToList();
        long freed = kept.Sum(session => session.FreedBytes);
        int changes = kept.Sum(session => session.Entries.Count(entry => entry.Completed && entry.IsReversible));

        var rhythm = RhythmOf(kept, now, timeZone);

        var change = SpeedCheck.LastChange(kept);
        TimeSpan? saved = performance is not null && change is not null
            ? SpeedCheck.Compare(performance.Boots, change.At)?.Saved
            : null;

        var recent = performance is null ? [] : SpeedCheck.Recent(performance.Boots);
        TimeSpan? latestBoot = recent.Count > 0 ? recent[0].Duration : null;

        Milestone[] milestones =
        [
            new(FirstTuneUp, kept.Count > 0, kept.Count > 0 ? 1 : 0),
            new(TenGigabytes, freed >= TenGigabytesInBytes, Fraction(freed, TenGigabytesInBytes)),
            new(SpeedTuneUp, change is not null, change is not null ? 1 : 0),
            new(UnderFortySeconds, latestBoot is TimeSpan boot && boot < FortySeconds, latestBoot is TimeSpan b ? Fraction(FortySeconds.TotalSeconds, b.TotalSeconds) : null),
            new(TenSecondsFaster, saved >= TenSeconds, saved is TimeSpan s ? Fraction(s.TotalSeconds, TenSeconds.TotalSeconds) : null),
            new(LeanStartup, startupAppsOn <= LeanStartupApps, startupAppsOn is int on ? Fraction(LeanStartupApps, Math.Max(on, 1)) : null),
            new(ThreeMonths, rhythm.Streak >= 3, Fraction(rhythm.Streak, 3)),
            new(SixMonths, rhythm.Streak >= 6, Fraction(rhythm.Streak, 6)),
        ];

        return new MilestoneProgress(freed, changes, saved, rhythm, milestones);
    }

    private static Rhythm RhythmOf(IReadOnlyList<SessionManifest> kept, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var done = kept
            .Select(session => TimeZoneInfo.ConvertTime(session.StartedAt, timeZone))
            .Select(local => (local.Year, local.Month))
            .ToHashSet();

        var local = TimeZoneInfo.ConvertTime(now, timeZone);
        var current = new DateTime(local.Year, local.Month, 1);
        bool doneThisMonth = done.Contains((current.Year, current.Month));

        // This month isn't missed until it's over, so a run counts back from last month when
        // nothing has happened yet this month.
        var cursor = doneThisMonth ? current : current.AddMonths(-1);
        int streak = 0;
        while (done.Contains((cursor.Year, cursor.Month)))
        {
            streak++;
            cursor = cursor.AddMonths(-1);
        }

        var months = Enumerable.Range(0, MonthsShown)
            .Select(offset => current.AddMonths(offset - (MonthsShown - 1)))
            .Select(month => new MonthMark(month.Year, month.Month, done.Contains((month.Year, month.Month)), month == current))
            .ToList();

        return new Rhythm(streak, months, doneThisMonth);
    }

    private static double Fraction(double value, double target) => target <= 0 ? 1 : Math.Clamp(value / target, 0, 1);
}
