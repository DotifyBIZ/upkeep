namespace Upkeep.App.Core.Cleanup;

/// <summary>
/// One category the user chose to clean, with the evidence the choice was made from. A plan is
/// built from scans and selections only — it never re-reads the disk, so what executes is exactly
/// what was previewed (ADR-0006).
/// </summary>
public sealed record PlannedCategory(JunkCategoryScan Scan)
{
    public JunkCategory Category => JunkCatalog.Get(Scan.CategoryId);

    public long TotalBytes => Scan.TotalBytes;

    public long ItemCount => Scan.ItemCount;
}

/// <summary>
/// What a cleanup run will do, in the order it will do it. This is the object the confirmation
/// dialog describes and the executor consumes — the two can never disagree about scope because
/// there is only one of it.
/// </summary>
public sealed record CleanupPlan
{
    public required IReadOnlyList<PlannedCategory> Categories { get; init; }

    /// <summary>Total the preview promises. Categories whose size only Windows knows (the
    /// component store) contribute nothing here rather than a guess.</summary>
    public long EstimatedBytes => Categories.Sum(category => category.TotalBytes);

    public long ItemCount => Categories.Sum(category => category.ItemCount);

    public bool IsEmpty => Categories.Count == 0;

    /// <summary>True when any chosen category needs the elevated helper — which is what decides
    /// whether the user sees a UAC prompt at all.</summary>
    public bool RequiresElevation => Categories.Any(category => category.Category.RequiresElevation);

    /// <summary>
    /// True when the run includes something Windows cannot undo. Drives both the "can't be undone"
    /// wording in the confirmation and whether a restore point is created first.
    /// </summary>
    public bool HasIrreversibleWork => Categories.Any(category => category.Category.Removal == RemovalKind.Irreversible);

    /// <summary>True when part of the run moves files to quarantine rather than deleting them —
    /// worth saying in the confirmation, because it is the opposite promise from the rest.</summary>
    public bool HasQuarantinedWork => Categories.Any(category => category.Category.Removal == RemovalKind.Quarantined);

    /// <summary>
    /// A restore point precedes any run that touches the system, which is the case worth being able
    /// to roll back: a user-scope cache clean has nothing a restore point would help with.
    /// </summary>
    public bool NeedsRestorePoint => Categories.Any(category =>
        category.Category.Scope == JunkScope.System || category.Category.Removal == RemovalKind.Irreversible);

    public IEnumerable<PlannedCategory> ForScope(JunkScope scope) =>
        Categories.Where(category => category.Category.Scope == scope);

    /// <summary>
    /// Builds a plan from scans and the ids the user left selected. Empty categories are dropped:
    /// a plan should contain work, not a list of things that turned out to have nothing to do.
    /// </summary>
    public static CleanupPlan From(IEnumerable<JunkCategoryScan> scans, IReadOnlySet<JunkCategoryId> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(scans);
        ArgumentNullException.ThrowIfNull(selectedIds);

        var categories = scans
            .Where(scan => selectedIds.Contains(scan.CategoryId))
            .Where(scan => !scan.IsEmpty)
            .Select(scan => new PlannedCategory(scan))
            .OrderBy(category => category.Category.Scope)
            .ToList();

        return new CleanupPlan { Categories = categories };
    }

    public static readonly CleanupPlan Empty = new() { Categories = [] };
}
