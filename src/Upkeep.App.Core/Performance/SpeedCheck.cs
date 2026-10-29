using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Performance;

/// <summary>The most recent set of speed changes still in place: when it happened, and how many.</summary>
public sealed record SpeedChange(DateTimeOffset At, int ChangeCount, string SessionId);

/// <summary>Starts before a change against starts since, both as Windows timed them.</summary>
public sealed record SpeedComparison(TimeSpan BeforeAverage, int BeforeCount, TimeSpan AfterAverage, int AfterCount)
{
    /// <summary>Positive when starts got faster. Can be negative, and is shown as such.</summary>
    public TimeSpan Saved => BeforeAverage - AfterAverage;

    /// <summary>Fewer starts than this since the change, and the page says the sample is small.</summary>
    public const int SmallSample = 3;

    public bool IsSmallSample => AfterCount < SmallSample;
}

/// <summary>
/// The arithmetic behind Speed check: averages, the last change, and before-against-after.
/// <para>
/// Nothing here estimates. A saving exists only once Windows has timed a start after the change
/// (ADR-0010).
/// </para>
/// </summary>
public static class SpeedCheck
{
    /// <summary>How many starts either side of a change are averaged — and how many Speed check charts.</summary>
    public const int Window = 10;

    public static TimeSpan? Average(IEnumerable<BootRecord> boots)
    {
        ArgumentNullException.ThrowIfNull(boots);

        var list = boots.ToList();
        return list.Count == 0 ? null : TimeSpan.FromMilliseconds(list.Average(boot => boot.Duration.TotalMilliseconds));
    }

    /// <summary>The newest <see cref="Window"/> starts, newest first.</summary>
    public static IReadOnlyList<BootRecord> Recent(IEnumerable<BootRecord> boots)
    {
        ArgumentNullException.ThrowIfNull(boots);
        return [.. boots.OrderByDescending(boot => boot.At).Take(Window)];
    }

    /// <summary>
    /// The latest session that changed something affecting speed and hasn't been reverted, timed
    /// from its first such change. Null when there isn't one.
    /// </summary>
    public static SpeedChange? LastChange(IEnumerable<SessionManifest> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        return sessions
            .Where(session => session.RevertedAt is null)
            .Select(session => (Session: session, Changes: session.Entries.Where(entry => entry.Completed && AffectsSpeed(entry)).ToList()))
            .Where(candidate => candidate.Changes.Count > 0)
            .Select(candidate => new SpeedChange(candidate.Changes.Min(entry => entry.At), candidate.Changes.Count, candidate.Session.Id))
            .OrderByDescending(change => change.At)
            .FirstOrDefault();
    }

    /// <summary>Before against after; null until there is at least one start on each side.</summary>
    public static SpeedComparison? Compare(IEnumerable<BootRecord> boots, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(boots);

        var list = boots.ToList();
        var before = Recent(list.Where(boot => boot.At < changedAt));
        var after = Recent(list.Where(boot => boot.At > changedAt));

        return Average(before) is TimeSpan beforeAverage && Average(after) is TimeSpan afterAverage
            ? new SpeedComparison(beforeAverage, before.Count, afterAverage, after.Count)
            : null;
    }

    /// <summary>
    /// What counts as a speed change: something turned off at startup or in the background, a
    /// service start type, or one of the Performance tab's settings. Turning something back on is
    /// a change too — it would explain a start getting slower.
    /// </summary>
    private static bool AffectsSpeed(SessionEntry entry) => entry switch
    {
        StartupItemToggledEntry or ScheduledTaskToggledEntry or ServiceStartTypeChangedEntry or BackgroundAccessChangedEntry => true,
        SystemSettingChangedEntry setting => setting.SettingId is PerformanceSettingIds.Animations
            or PerformanceSettingIds.Transparency
            or PerformanceSettingIds.PowerPlan
            or PerformanceSettingIds.PowerModePluggedIn
            or PerformanceSettingIds.PowerModeOnBattery,
        _ => false,
    };
}
