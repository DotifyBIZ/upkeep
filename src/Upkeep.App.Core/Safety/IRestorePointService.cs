namespace Upkeep.App.Core.Safety;

/// <summary>What happened when Upkeep asked Windows for a restore point.</summary>
public enum RestorePointStatus
{
    /// <summary>A new restore point was created for this run.</summary>
    Created,

    /// <summary>
    /// Windows refused to make one because it already made one in the last 24 hours. The existing
    /// point covers this session too, so it is recorded rather than pretending a new one exists.
    /// </summary>
    ReusedRecent,

    /// <summary>System Protection was off and could not be turned on (policy, or a machine where
    /// it isn't available). The run continues; the user is told afterwards.</summary>
    Unavailable,

    /// <summary>Creating one needs administrator rights that weren't granted.</summary>
    NotElevated,
}

/// <param name="Status">What Windows did.</param>
/// <param name="Description">The restore point's description, for the results screen and History.</param>
/// <param name="ProtectionWasEnabled">
/// True when Upkeep turned System Protection on to make this possible. Reported after the fact
/// rather than asked about beforehand, per the product decision recorded in ADR-0006.
/// </param>
public sealed record RestorePointResult(RestorePointStatus Status, string? Description = null, bool ProtectionWasEnabled = false)
{
    /// <summary>True when a restore point — new or recent — covers the session about to run.</summary>
    public bool IsCovered => Status is RestorePointStatus.Created or RestorePointStatus.ReusedRecent;
}

/// <summary>
/// Creates the System Restore point that precedes any destructive system-level run (ADR-0006).
/// Implemented against Windows' own System Restore API; the elevated helper does the actual work,
/// since creating a restore point requires administrator rights.
/// </summary>
public interface IRestorePointService
{
    /// <summary>Whether System Protection is currently on for the system drive.</summary>
    Task<bool> IsProtectionEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure a restore point covers what is about to happen: turns System Protection on if it
    /// is off, then creates a point — or reports the recent one Windows would rather keep.
    /// </summary>
    Task<RestorePointResult> EnsureRestorePointAsync(string description, CancellationToken cancellationToken = default);
}
