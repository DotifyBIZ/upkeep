using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Cleanup;

/// <summary>
/// Scans the machine-wide junk categories. This runs **inside the elevated helper** — the shell
/// has no business walking these paths, and on a standard-user account it couldn't anyway.
/// <para>
/// It is also what makes the helper's re-derivation rule possible (ADR-0005): when a clean request
/// arrives naming a category, the helper runs this scanner itself and removes only files that
/// appear in both its own result and the request. A path the helper didn't derive is never
/// touched, whatever the shell sends.
/// </para>
/// </summary>
public sealed class SystemJunkScanner
{
    private readonly IWellKnownPaths _paths;
    private readonly TimeProvider _timeProvider;

    public SystemJunkScanner(IWellKnownPaths paths, TimeProvider? timeProvider = null)
    {
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Scans one machine-wide category.
    /// </summary>
    /// <param name="categoryId">The category to measure.</param>
    /// <param name="excludeProfilePath">
    /// The profile to leave out of <see cref="JunkCategoryId.OtherUsersTemp"/> — the account
    /// actually using Upkeep. The helper resolves this from the shell process's owner, not from
    /// its own identity: with over-the-shoulder elevation those are different people.
    /// </param>
    public JunkCategoryScan Scan(JunkCategoryId categoryId, string? excludeProfilePath = null, CancellationToken cancellationToken = default)
    {
        var category = JunkCatalog.Get(categoryId);
        if (category.Scope != JunkScope.System)
        {
            throw new ArgumentOutOfRangeException(
                nameof(categoryId),
                categoryId,
                "User-scope categories are scanned by the shell, not the helper.");
        }

        return categoryId switch
        {
            JunkCategoryId.WindowsTemp => ScanWindowsTemp(cancellationToken),
            JunkCategoryId.OtherUsersTemp => ScanOtherUsersTemp(excludeProfilePath, cancellationToken),
            JunkCategoryId.SystemCrashDumps => ScanSystemCrashDumps(cancellationToken),
            JunkCategoryId.DeliveryOptimization => ScanDeliveryOptimizationCache(cancellationToken),

            // Nothing to enumerate: DISM decides what is superseded, and only reports what it
            // removed afterwards. The preview says so rather than inventing a number.
            JunkCategoryId.WindowsUpdateCleanup => JunkCategoryScan.Empty(categoryId, JunkScanNote.SizeKnownAfterCleanup),

            _ => JunkCategoryScan.Empty(categoryId),
        };
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private JunkCategoryScan ScanWindowsTemp(CancellationToken cancellationToken)
    {
        string windowsTemp = Path.Combine(_paths.WindowsDirectory, "Temp");
        var scan = FileTreeScanner.Scan(windowsTemp, JunkCatalog.RecentTempFileGrace, UtcNow, cancellationToken);

        return new JunkCategoryScan(
            JunkCategoryId.WindowsTemp,
            scan.Items,
            scan.HadUnreadableEntries ? JunkScanNote.PartiallyUnreadable : JunkScanNote.None,
            scan.SkippedCount);
    }

    /// <summary>
    /// Every other account's %TEMP%. Off by default in the catalog: touching another person's
    /// files is a different decision than cleaning your own, even when it is only temp files.
    /// </summary>
    private JunkCategoryScan ScanOtherUsersTemp(string? excludeProfilePath, CancellationToken cancellationToken)
    {
        var items = new List<JunkItem>();
        var details = new List<string>();
        int skipped = 0;
        bool unreadable = false;

        foreach (string profile in SafeEnumerateDirectories(_paths.UserProfilesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludeProfilePath is not null
                && string.Equals(Path.TrimEndingDirectorySeparator(profile), Path.TrimEndingDirectorySeparator(excludeProfilePath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Service accounts keep profiles here too; they have no user temp worth cleaning and
            // no person to be surprised by it.
            string name = Path.GetFileName(profile);
            if (name is "Public" or "Default" or "Default User" or "All Users")
            {
                continue;
            }

            string temp = Path.Combine(profile, "AppData", "Local", "Temp");
            var scan = FileTreeScanner.Scan(temp, JunkCatalog.RecentTempFileGrace, UtcNow, cancellationToken);
            if (scan.Items.Count == 0)
            {
                continue;
            }

            items.AddRange(scan.Items);
            skipped += scan.SkippedCount;
            unreadable |= scan.HadUnreadableEntries;
            details.Add(name);
        }

        return new JunkCategoryScan(
            JunkCategoryId.OtherUsersTemp,
            items,
            unreadable ? JunkScanNote.PartiallyUnreadable : JunkScanNote.None,
            skipped)
        {
            Details = details,
        };
    }

    private JunkCategoryScan ScanSystemCrashDumps(CancellationToken cancellationToken)
    {
        string[] folders =
        [
            Path.Combine(_paths.WindowsDirectory, "Minidump"),
            Path.Combine(_paths.WindowsDirectory, "LiveKernelReports"),
            Path.Combine(_paths.ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(_paths.ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
        ];

        var items = new List<JunkItem>();
        int skipped = 0;
        bool unreadable = false;

        foreach (string folder in folders)
        {
            var scan = FileTreeScanner.Scan(folder, TimeSpan.Zero, UtcNow, cancellationToken);
            items.AddRange(scan.Items);
            skipped += scan.SkippedCount;
            unreadable |= scan.HadUnreadableEntries;
        }

        // The full-memory dump is a single file rather than a folder, and is usually the largest
        // thing in this category by a wide margin.
        string memoryDump = Path.Combine(_paths.WindowsDirectory, "MEMORY.DMP");
        try
        {
            var info = new FileInfo(memoryDump);
            if (info.Exists)
            {
                items.Add(new JunkItem(info.FullName, info.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable = true;
        }

        return new JunkCategoryScan(
            JunkCategoryId.SystemCrashDumps,
            items,
            unreadable ? JunkScanNote.PartiallyUnreadable : JunkScanNote.None,
            skipped);
    }

    /// <summary>
    /// Update pieces Windows keeps to share with other PCs. Measured here so the preview has a
    /// real number; the removal itself goes through Windows' own Delivery Optimization cmdlet
    /// rather than deleting the folder by hand.
    /// </summary>
    private JunkCategoryScan ScanDeliveryOptimizationCache(CancellationToken cancellationToken)
    {
        string[] folders =
        [
            Path.Combine(_paths.WindowsDirectory, "SoftwareDistribution", "DeliveryOptimization"),
            Path.Combine(_paths.WindowsDirectory, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization"),
        ];

        var items = new List<JunkItem>();
        int skipped = 0;
        bool unreadable = false;

        foreach (string folder in folders)
        {
            var scan = FileTreeScanner.Scan(folder, TimeSpan.Zero, UtcNow, cancellationToken);
            items.AddRange(scan.Items);
            skipped += scan.SkippedCount;
            unreadable |= scan.HadUnreadableEntries;
        }

        return new JunkCategoryScan(
            JunkCategoryId.DeliveryOptimization,
            items,
            unreadable ? JunkScanNote.PartiallyUnreadable : JunkScanNote.None,
            skipped);
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
