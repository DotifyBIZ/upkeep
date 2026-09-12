using Upkeep.App.Core.Safety;

namespace Upkeep.App.Core.Cleanup;

/// <summary>Progress during a run, for the UI to show something honest while it waits.</summary>
/// <param name="CategoryId">The category being worked on.</param>
/// <param name="CompletedCategories">How many categories are finished.</param>
/// <param name="TotalCategories">How many the plan contains.</param>
public sealed record CleanupProgress(JunkCategoryId CategoryId, int CompletedCategories, int TotalCategories);

/// <summary>What one category actually managed to do, which is not always what was planned.</summary>
public sealed record CategoryOutcome(JunkCategoryId CategoryId, long FreedBytes, long ItemsRemoved, long ItemsSkipped)
{
    /// <summary>True when nothing could be removed at all — every item was in use or refused.</summary>
    public bool WasBlocked => ItemsRemoved == 0 && ItemsSkipped > 0;
}

/// <summary>
/// The results moment: what a run freed, what it left behind, and what covers it if the user wants
/// it back. Deliberately reports skipped items rather than quietly rounding them away — files that
/// were in use are the difference between the estimate and the outcome.
/// </summary>
public sealed record CleanupOutcome
{
    public required string SessionId { get; init; }

    public required IReadOnlyList<CategoryOutcome> Categories { get; init; }

    public RestorePointResult? RestorePoint { get; init; }

    /// <summary>Set when the user declined the administrator prompt, so system-wide items were
    /// left out of an otherwise successful run.</summary>
    public bool ElevationDeclined { get; init; }

    /// <summary>Free space on the system drive after the run, for "now has X free".</summary>
    public long? SystemDriveFreeBytes { get; init; }

    public long FreedBytes => Categories.Sum(category => category.FreedBytes);

    public long ItemsRemoved => Categories.Sum(category => category.ItemsRemoved);

    public long ItemsSkipped => Categories.Sum(category => category.ItemsSkipped);
}
