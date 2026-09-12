namespace Upkeep.App.Core.Cleanup;

/// <summary>One file a scan found, with the size it would free.</summary>
public sealed record JunkItem(string Path, long SizeBytes);

/// <summary>Why a category found less than the user might expect — surfaced in the preview rather
/// than silently reported as a smaller number.</summary>
public enum JunkScanNote
{
    None,

    /// <summary>A browser is running, so its cache is locked and was left out of the scan.</summary>
    BrowserRunning,

    /// <summary>The size is only known once Windows has done the work (component store cleanup).</summary>
    SizeKnownAfterCleanup,

    /// <summary>Scanning this category needs administrator rights, which haven't been granted.</summary>
    NeedsElevation,

    /// <summary>Some files couldn't be read; they are excluded from both the size and the plan.</summary>
    PartiallyUnreadable,
}

/// <summary>
/// What one category's scan found. A scan never changes anything — this is the evidence the user
/// reviews before a plan is built from it (ADR-0006).
/// <para>
/// Most categories report a list of files. Two can't: the Recycle Bin, where the shell gives a
/// total rather than paths, and the component store, whose size only Windows knows and only after
/// the fact. Those set <see cref="ReportedBytes"/> instead, which is why size and count are not
/// simply derived from <see cref="Items"/>.
/// </para>
/// </summary>
public sealed record JunkCategoryScan(
    JunkCategoryId CategoryId,
    IReadOnlyList<JunkItem> Items,
    JunkScanNote Note = JunkScanNote.None,
    int SkippedCount = 0)
{
    /// <summary>Size reported by Windows rather than measured file by file. Null for the usual case.</summary>
    public long? ReportedBytes { get; init; }

    /// <summary>Item count reported by Windows rather than counted. Null for the usual case.</summary>
    public long? ReportedItemCount { get; init; }

    /// <summary>Extra lines the UI shows under the category — which browser profiles were found,
    /// which accounts' temp folders, and so on.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    public long TotalBytes => ReportedBytes ?? Items.Sum(item => item.SizeBytes);

    public long ItemCount => ReportedItemCount ?? Items.Count;

    /// <summary>True when there is nothing for this category to do.</summary>
    public bool IsEmpty => TotalBytes == 0 && ItemCount == 0 && Note != JunkScanNote.SizeKnownAfterCleanup;

    public static JunkCategoryScan Empty(JunkCategoryId categoryId, JunkScanNote note = JunkScanNote.None) =>
        new(categoryId, [], note);
}
