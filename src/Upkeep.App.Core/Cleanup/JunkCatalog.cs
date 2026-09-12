namespace Upkeep.App.Core.Cleanup;

/// <summary>
/// Every category Upkeep will clean, and the rules that go with it. Kept as one readable table
/// rather than spread across the scanners: what a maintenance tool is willing to delete should be
/// reviewable at a glance.
/// </summary>
public static class JunkCatalog
{
    /// <summary>
    /// Temp files younger than this are left alone. An installer that is running right now keeps
    /// its working files in %TEMP%, and deleting them mid-install breaks the install — the most
    /// common way a cleaner does real damage.
    /// </summary>
    public static readonly TimeSpan RecentTempFileGrace = TimeSpan.FromHours(24);

    public static IReadOnlyList<JunkCategory> All { get; } =
    [
        // Your account — no elevation, cleaned by the shell itself.
        new(JunkCategoryId.UserTemp, JunkScope.User, RemovalKind.Deleted, SelectedByDefault: true),
        new(JunkCategoryId.BrowserCache, JunkScope.User, RemovalKind.Deleted, SelectedByDefault: true),
        new(JunkCategoryId.ThumbnailCache, JunkScope.User, RemovalKind.Deleted, SelectedByDefault: true),
        new(JunkCategoryId.ShaderCache, JunkScope.User, RemovalKind.Deleted, SelectedByDefault: true),
        new(JunkCategoryId.UserCrashDumps, JunkScope.User, RemovalKind.Deleted, SelectedByDefault: true),

        // Emptying the bin is the user's own deletion carried out; there is no way back, so it is
        // labelled irreversible and still selected by default — it is what "clean up" means.
        new(JunkCategoryId.RecycleBin, JunkScope.User, RemovalKind.Irreversible, SelectedByDefault: true),

        // System-wide — scanned and cleaned only through the elevated helper.
        new(JunkCategoryId.WindowsTemp, JunkScope.System, RemovalKind.Deleted, SelectedByDefault: true),

        // Off by default: other people's files on a shared machine are a different decision than
        // cleaning your own, even when they're only temp files.
        new(JunkCategoryId.OtherUsersTemp, JunkScope.System, RemovalKind.Deleted, SelectedByDefault: false),

        // DISM decides what is superseded and removes it; the space it frees is only known
        // afterwards, and previously-installed updates can no longer be uninstalled.
        new(JunkCategoryId.WindowsUpdateCleanup, JunkScope.System, RemovalKind.Irreversible, SelectedByDefault: true),
        new(JunkCategoryId.DeliveryOptimization, JunkScope.System, RemovalKind.Deleted, SelectedByDefault: true),
        new(JunkCategoryId.SystemCrashDumps, JunkScope.System, RemovalKind.Deleted, SelectedByDefault: true),
    ];

    public static JunkCategory Get(JunkCategoryId id) =>
        All.FirstOrDefault(category => category.Id == id)
        ?? throw new ArgumentOutOfRangeException(nameof(id), id, "No such junk category.");

    public static IEnumerable<JunkCategory> ForScope(JunkScope scope) => All.Where(category => category.Scope == scope);
}
