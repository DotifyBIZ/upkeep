namespace Upkeep.App.Core.Cleanup;

/// <summary>The junk categories Upkeep knows how to clean. Stable identifiers: they appear in
/// session journals on disk, so renaming one breaks the history of past runs.</summary>
public enum JunkCategoryId
{
    UserTemp,
    BrowserCache,
    ThumbnailCache,
    ShaderCache,
    UserCrashDumps,
    RecycleBin,
    WindowsTemp,
    OtherUsersTemp,
    WindowsUpdateCleanup,
    DeliveryOptimization,
    SystemCrashDumps,
}

/// <summary>Whether a category can be cleaned as the signed-in user, or needs the elevated helper.</summary>
public enum JunkScope
{
    /// <summary>Inside the user's own profile. No elevation, and the shell does the work itself.</summary>
    User,

    /// <summary>Machine-wide. Scanned and cleaned only through the elevated helper (ADR-0005).</summary>
    System,
}

/// <summary>
/// What happens to the items in a category when a plan runs — shown in the preview so the choice
/// is the user's, not a surprise (ADR-0006).
/// </summary>
public enum RemovalKind
{
    /// <summary>Deleted outright. Only ever caches and temporary files, which rebuild themselves.</summary>
    Deleted,

    /// <summary>Moved to quarantine and restorable for the retention period.</summary>
    Quarantined,

    /// <summary>Handed to Windows, which does not offer a way back (emptying the Recycle Bin,
    /// component store cleanup). Labelled as such before the user confirms.</summary>
    Irreversible,
}

/// <summary>
/// One cleanable category: what it is, where it lives, and what cleaning it costs the user. The
/// definitions are data (see <see cref="JunkCatalog"/>) so the rules are readable in one place
/// rather than spread across scanners.
/// </summary>
public sealed record JunkCategory(
    JunkCategoryId Id,
    JunkScope Scope,
    RemovalKind Removal,
    bool SelectedByDefault)
{
    /// <summary>Resource key for the category's name, resolved by the shell.</summary>
    public string NameKey => $"JunkCategory{Id}Name";

    /// <summary>Resource key for the one-line explanation of what is lost by cleaning it.</summary>
    public string DescriptionKey => $"JunkCategory{Id}Description";

    public bool RequiresElevation => Scope == JunkScope.System;
}
