namespace Upkeep.App.Core.Files;

/// <summary>
/// Walks the folders a file scan was pointed at, applying the rules that keep the results honest
/// and the scan cheap:
/// <list type="bullet">
///   <item>Cloud placeholders are skipped. Reading a OneDrive file that isn't downloaded pulls it
///   down — a scan that quietly fills the disk it was asked to free is the worst kind of bug.</item>
///   <item>Reparse points are not followed, so a junction can't walk the scan out of its scope or
///   count the same file twice.</item>
///   <item>Windows, Program Files and app-data folders are never entered: files in there are
///   duplicated for a reason, and "reclaiming" them breaks the app that put them there.</item>
/// </list>
/// </summary>
public static class FileEnumerator
{
    private const FileAttributes CloudPlaceholder =
        FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

    /// <summary>Enumerates every file in scope, largest-first is *not* guaranteed — callers group.</summary>
    /// <param name="scope">Where to look, what to skip, and the size floor.</param>
    /// <param name="skippedCloudFiles">How many files were left alone because they live in the cloud.</param>
    public static IReadOnlyList<ScannedFile> Enumerate(
        FileScanScope scope,
        out int skippedCloudFiles,
        IProgress<FileScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var files = new List<ScannedFile>();
        var exclusions = BuildExclusionSet(scope);
        int cloudSkipped = 0;

        foreach (string root in scope.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Walk(root, scope, exclusions, files, ref cloudSkipped, progress, cancellationToken);
        }

        skippedCloudFiles = cloudSkipped;
        return files;
    }

    private static HashSet<string> BuildExclusionSet(FileScanScope scope)
    {
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string exclusion in scope.Exclusions)
        {
            exclusions.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(exclusion)));
        }

        return exclusions;
    }

    private static void Walk(
        string folder,
        FileScanScope scope,
        HashSet<string> exclusions,
        List<ScannedFile> files,
        ref int cloudSkipped,
        IProgress<FileScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(folder) || IsExcluded(folder, exclusions))
        {
            return;
        }

        progress?.Report(new FileScanProgress(folder, files.Count));

        var directory = new DirectoryInfo(folder);

        try
        {
            foreach (var file in directory.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if ((file.Attributes & CloudPlaceholder) != 0)
                    {
                        cloudSkipped++;
                        continue;
                    }

                    if (file.Length < scope.MinimumFileBytes)
                    {
                        continue;
                    }

                    files.Add(new ScannedFile(file.FullName, file.Length, file.LastWriteTimeUtc));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that vanished or refused between enumeration and inspection.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        IEnumerable<DirectoryInfo> subdirectories;
        try
        {
            subdirectories = directory.EnumerateDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            try
            {
                if (subdirectory.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || FileScanScope.AlwaysExcludedFolderNames.Contains(subdirectory.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            Walk(subdirectory.FullName, scope, exclusions, files, ref cloudSkipped, progress, cancellationToken);
        }
    }

    private static bool IsExcluded(string folder, HashSet<string> exclusions)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return exclusions.Contains(full)
            || FileScanScope.AlwaysExcludedFolderNames.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase);
    }
}
