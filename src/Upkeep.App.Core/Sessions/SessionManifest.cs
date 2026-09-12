namespace Upkeep.App.Core.Sessions;

/// <summary>Which part of the app a session came from. Persisted, so the names are stable.</summary>
public enum SessionKind
{
    Cleanup,
    Files,
    Apps,
    Startup,
    Drivers,
}

/// <summary>
/// The record of one run: what was done, what it freed, and what can be put back. Written as it
/// happens rather than at the end (ADR-0006) — a manifest that only exists after a successful run
/// is no use to the run that didn't finish.
/// </summary>
public sealed record SessionManifest
{
    /// <summary>Sortable, filename-safe, and unique per run.</summary>
    public required string Id { get; init; }

    public required SessionKind Kind { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The restore point covering this session, if one was created or reused.</summary>
    public string? RestorePointDescription { get; init; }

    /// <summary>True when the restore point was one Windows had already made in the last 24 hours
    /// rather than a new one — the user is told which, rather than being told a new one exists.</summary>
    public bool RestorePointWasReused { get; init; }

    /// <summary>Set when a session was reverted, so History doesn't offer to revert it twice.</summary>
    public DateTimeOffset? RevertedAt { get; init; }

    public IReadOnlyList<SessionEntry> Entries { get; init; } = [];

    public long FreedBytes => Entries.Where(entry => entry.Completed).Sum(entry => entry.FreedBytes);

    public long QuarantinedBytes => Entries
        .OfType<FileQuarantinedEntry>()
        .Where(entry => entry.Completed)
        .Sum(entry => entry.SizeBytes);

    public int CompletedCount => Entries.Count(entry => entry.Completed);

    public int FailedCount => Entries.Count(entry => entry.FailureDetail is not null);

    /// <summary>True when anything in this session can still be put back.</summary>
    public bool CanRevert => RevertedAt is null && Entries.Any(entry => entry.Completed && entry.IsReversible);

    public static string NewId(DateTimeOffset startedAt) =>
        $"{startedAt.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
}
