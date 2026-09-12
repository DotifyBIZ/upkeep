namespace Upkeep.App.Core.Safety;

/// <summary>
/// The decisions around a restore point, kept separate from the WMI calls that carry them out so
/// they can be tested without creating restore points on the machine running the tests.
/// </summary>
public static class RestorePointPolicy
{
    /// <summary>
    /// Windows refuses to create a second restore point within this window. It is a Windows
    /// setting, not Upkeep's — and Upkeep does not change it: the existing point covers the same
    /// ground, and quietly loosening a system-wide protection setting to get a tidier log entry is
    /// exactly the kind of thing a maintenance tool shouldn't do behind the user's back.
    /// </summary>
    public static readonly TimeSpan CreationThrottle = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether an existing point is recent enough that Windows will refuse a new one — in which
    /// case the session records the point that already covers it, rather than claiming a new one.
    /// </summary>
    public static bool CoversThisRun(DateTimeOffset? latestPointCreatedAt, DateTimeOffset now) =>
        latestPointCreatedAt is { } created && now - created < CreationThrottle;

    /// <summary>Reads the outcome of an attempt, given what existed before and after.</summary>
    /// <param name="createdNewPoint">True when Windows reported a new point was made.</param>
    /// <param name="latestPointCreatedAt">The newest restore point known after the attempt.</param>
    /// <param name="now">Reference time.</param>
    public static RestorePointStatus Evaluate(bool createdNewPoint, DateTimeOffset? latestPointCreatedAt, DateTimeOffset now)
    {
        if (createdNewPoint)
        {
            return RestorePointStatus.Created;
        }

        return CoversThisRun(latestPointCreatedAt, now)
            ? RestorePointStatus.ReusedRecent
            : RestorePointStatus.Unavailable;
    }
}
