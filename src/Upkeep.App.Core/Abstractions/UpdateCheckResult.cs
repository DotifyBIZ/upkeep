namespace Upkeep.App.Core.Abstractions;

/// <summary>
/// What an update check found. <see cref="CheckFailed"/> is the difference between "there is
/// nothing newer" and "nobody managed to ask" — GitHub rate-limits unauthenticated callers, and
/// reporting that 403 as "you're up to date" tells the user something Upkeep does not know.
/// </summary>
public sealed record UpdateCheckResult(bool IsUpdateAvailable, string? LatestVersion, string? ReleaseUrl)
{
    /// <summary>True when the check could not be completed at all.</summary>
    public bool CheckFailed { get; init; }

    public static readonly UpdateCheckResult NoUpdate = new(false, null, null);

    /// <summary>Offline, rate-limited, timed out, or an answer that could not be read.</summary>
    public static readonly UpdateCheckResult Failed = new(false, null, null) { CheckFailed = true };
}
