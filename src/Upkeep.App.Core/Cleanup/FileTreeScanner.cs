namespace Upkeep.App.Core.Cleanup;

/// <summary>What a scan of one folder found, and what it deliberately left behind.</summary>
public sealed record FileTreeScan(IReadOnlyList<JunkItem> Items, int SkippedCount, bool HadUnreadableEntries)
{
    public long TotalBytes => Items.Sum(item => item.SizeBytes);

    public static readonly FileTreeScan Empty = new([], 0, false);
}

/// <summary>
/// Walks a folder and reports the files inside it, applying the rules that keep a cleaner from
/// doing damage. Every skip here exists because ignoring it breaks something real:
/// <list type="bullet">
///   <item>Files younger than the grace period belong to something running right now — deleting an
///   installer's working files mid-install is the classic way a cleaner ruins an afternoon.</item>
///   <item>Reparse points (junctions, symlinks) are not followed: Windows fills %TEMP%-adjacent
///   trees with them, and following one walks straight out of the folder being cleaned.</item>
///   <item>Cloud placeholders are left alone. Reading a OneDrive file that isn't downloaded pulls
///   it down — a "cleanup" that fills the disk and the user's bandwidth.</item>
/// </list>
/// </summary>
public static class FileTreeScanner
{
    /// <summary>
    /// Files whose content lives in the cloud rather than on this disk. Windows defines
    /// FILE_ATTRIBUTE_RECALL_ON_OPEN (0x00040000) and FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    /// (0x00400000), which OneDrive sets on files it hasn't downloaded; .NET's FileAttributes
    /// enum doesn't name either, so the raw values are used.
    /// </summary>
    private const FileAttributes CloudPlaceholder =
        FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

    /// <summary>
    /// Enumerates <paramref name="root"/> recursively.
    /// </summary>
    /// <param name="root">Folder to scan. A missing folder is an empty result, not an error.</param>
    /// <param name="minimumAge">Files modified more recently than this are left alone. Pass
    /// <see cref="TimeSpan.Zero"/> for folders where age is irrelevant (a browser cache).</param>
    /// <param name="now">Reference time, injectable so the grace period is testable.</param>
    public static FileTreeScan Scan(string root, TimeSpan minimumAge, DateTime? now = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return FileTreeScan.Empty;
        }

        var cutoff = (now ?? DateTime.UtcNow) - minimumAge;
        var items = new List<JunkItem>();
        int skipped = 0;
        bool unreadable = false;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // Hidden and system files are included deliberately — most of a cache's size is in
            // them, and skipping them would under-report every category. Reparse points are the
            // one thing skipped outright: %TEMP% and AppData are full of junctions, and following
            // one walks straight out of the folder being cleaned.
            AttributesToSkip = FileAttributes.ReparsePoint,
            // A folder we can't open is a fact to report, not an exception to unwind a scan with.
            IgnoreInaccessible = true,
        };

        try
        {
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || (file.Attributes & CloudPlaceholder) != 0)
                    {
                        skipped++;
                        continue;
                    }

                    if (minimumAge > TimeSpan.Zero && file.LastWriteTimeUtc > cutoff)
                    {
                        skipped++;
                        continue;
                    }

                    items.Add(new JunkItem(file.FullName, file.Length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that vanished or refused to answer between enumeration and inspection.
                    unreadable = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable = true;
        }

        return new FileTreeScan(items, skipped, unreadable);
    }
}
