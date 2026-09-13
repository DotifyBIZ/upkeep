using System.Globalization;

namespace Upkeep.App.Core.Updates;

/// <summary>What Windows Update is currently set to do on this PC.</summary>
/// <param name="PausedUntil">When the current pause ends, or null when updates are not paused.</param>
/// <param name="DeferFeatureUpdatesDays">Days feature updates are held back. Zero means not held.</param>
/// <param name="DeferQualityUpdatesDays">Days quality updates are held back. Zero means not held.</param>
public sealed record WindowsUpdateState(
    DateTimeOffset? PausedUntil,
    int DeferFeatureUpdatesDays,
    int DeferQualityUpdatesDays)
{
    public bool IsPaused => PausedUntil is not null;

    public static readonly WindowsUpdateState NotConfigured = new(null, 0, 0);
}

/// <summary>
/// The registry locations and value shapes Windows Update uses, and the arithmetic around them.
/// <para>
/// Pure on purpose: the writes themselves are a thin adapter inside the elevated helper, but *what*
/// gets written — how long a pause may run, how the expiry time is spelled, which day counts
/// Windows will accept — is the part worth testing.
/// </para>
/// </summary>
public static class WindowsUpdatePolicy
{
    /// <summary>Where the Settings app records a pause. Machine-wide, so helper-only.</summary>
    public const string PauseKeyPath = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    /// <summary>Where the deferral policies live. Home reads these and ignores them.</summary>
    public const string DeferralKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public const string PauseStartValueName = "PauseUpdatesStartTime";

    public const string PauseExpiryValueName = "PauseUpdatesExpiryTime";

    public const string FeatureDeferValueName = "DeferFeatureUpdatesPeriodInDays";

    public const string QualityDeferValueName = "DeferQualityUpdatesPeriodInDays";

    /// <summary>
    /// Windows itself offers up to 35 days, and refuses to pause again until it has caught up.
    /// Offering more would be a control that silently does less than it says.
    /// </summary>
    public const int MaximumPauseDays = 35;

    /// <summary>Feature updates can be held for a year; quality updates for a month.</summary>
    public const int MaximumFeatureDeferralDays = 365;

    public const int MaximumQualityDeferralDays = 30;

    /// <summary>
    /// The ISO-8601 UTC shape Windows writes these as — "2026-09-19T08:00:00Z". Getting the format
    /// wrong means a pause Windows reads as absent.
    /// </summary>
    public const string TimeFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>Days a pause may actually run for, given what the user asked.</summary>
    public static int ClampPauseDays(int days) => Math.Clamp(days, 1, MaximumPauseDays);

    public static int ClampFeatureDeferralDays(int days) => Math.Clamp(days, 0, MaximumFeatureDeferralDays);

    public static int ClampQualityDeferralDays(int days) => Math.Clamp(days, 0, MaximumQualityDeferralDays);

    /// <summary>Formats an instant the way Windows Update stores it.</summary>
    public static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads one of those timestamps back. Anything unparseable reads as "not paused" rather than
    /// throwing — these values are written by Windows and by other tools, not only by Upkeep.
    /// </summary>
    public static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// The start and expiry a pause of <paramref name="days"/> should be written as.
    /// </summary>
    public static (string Start, string Expiry) PauseWindow(DateTimeOffset now, int days)
    {
        var start = now.ToUniversalTime();
        return (FormatTime(start), FormatTime(start.AddDays(ClampPauseDays(days))));
    }

    /// <summary>
    /// Whether a recorded expiry still holds. A pause whose time has passed is over, whatever the
    /// value still says — Windows clears these lazily.
    /// </summary>
    public static bool IsPauseActive(DateTimeOffset? expiry, DateTimeOffset now) => expiry > now;

    /// <summary>
    /// Days a recorded pause still has to run, for putting a session back. A pause whose time has
    /// already passed comes back as zero — resume — rather than as a fresh pause the user never
    /// asked for. Rounded up, because Windows stores an instant and this request takes whole days.
    /// </summary>
    public static int PauseDaysRemaining(DateTimeOffset? expiry, DateTimeOffset now) =>
        expiry is DateTimeOffset until && until > now
            ? ClampPauseDays((int)Math.Ceiling((until - now).TotalDays))
            : 0;

    /// <summary>
    /// How a deferral pair is written into a journal entry. On-disk data, so both ends go through
    /// this rather than spelling the separator twice.
    /// </summary>
    public static string FormatDeferral(int featureDays, int qualityDays) =>
        string.Create(CultureInfo.InvariantCulture, $"{featureDays}/{qualityDays}");

    /// <summary>
    /// Reads a deferral pair back. A journal written by another build, or hand-edited, reads as
    /// false rather than throwing — a revert skips what it cannot understand.
    /// </summary>
    public static bool TryParseDeferral(string? value, out int featureDays, out int qualityDays)
    {
        featureDays = 0;
        qualityDays = 0;

        string[] parts = (value ?? string.Empty).Split('/');

        // Both halves or neither: a value that parses only as far as the separator must not leave
        // a caller holding half a pair.
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int feature)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int quality))
        {
            return false;
        }

        featureDays = feature;
        qualityDays = quality;
        return true;
    }

}
