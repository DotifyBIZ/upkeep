namespace Upkeep.App.Core.Files;

/// <summary>What a disk usage scan produced, and what it couldn't see.</summary>
/// <param name="Root">The folder tree, with sizes rolled up.</param>
/// <param name="FilesExamined">How many files were measured.</param>
/// <param name="UnreadableFolders">
/// Folders Windows refused. Reported rather than swallowed: a treemap missing 40 GB of
/// ProgramData, with no explanation, sends people looking for a bug that isn't there.
/// </param>
public sealed record DiskUsageResult(UsageNode Root, int FilesExamined, int UnreadableFolders);

/// <summary>Measures where the space actually went.</summary>
public interface IDiskUsageScanner
{
    Task<DiskUsageResult> ScanAsync(string root, IProgress<FileScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Walks a folder and rolls sizes up into a tree the treemap can draw.
/// <para>
/// Only the largest files in each folder are kept as their own nodes; the rest are rolled into a
/// single "smaller files" node. A drive holds hundreds of thousands of files and a treemap can
/// show a few hundred — keeping them all would cost memory and a frozen UI to draw rectangles too
/// small to see.
/// </para>
/// </summary>
public sealed class DiskUsageScanner : IDiskUsageScanner
{
    /// <summary>Individual files kept per folder before the rest are aggregated.</summary>
    public const int MaxFileNodesPerFolder = 20;

    /// <summary>Name used for the aggregate node; the UI localizes it by this key.</summary>
    public const string AggregateNodeKey = "DiskUsageSmallerFiles";

    public Task<DiskUsageResult> ScanAsync(string root, IProgress<FileScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        return Task.Run(() =>
        {
            int filesExamined = 0;
            int unreadableFolders = 0;
            var node = Measure(root, ref filesExamined, ref unreadableFolders, progress, cancellationToken);

            return new DiskUsageResult(node, filesExamined, unreadableFolders);
        }, cancellationToken);
    }

    private static UsageNode Measure(
        string folder,
        ref int filesExamined,
        ref int unreadableFolders,
        IProgress<FileScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        if (string.IsNullOrEmpty(name))
        {
            // A drive root ("C:\") has no file name; show the root itself.
            name = folder;
        }

        var directory = new DirectoryInfo(folder);
        var children = new List<UsageNode>();
        long total = 0;

        progress?.Report(new FileScanProgress(folder, filesExamined));

        // Files first: keep the biggest as their own nodes, roll the rest into one.
        try
        {
            var files = new List<UsageNode>();
            long aggregateBytes = 0;
            int aggregateCount = 0;

            foreach (var file in directory.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    filesExamined++;
                    total += file.Length;
                    files.Add(new UsageNode(file.Name, file.FullName, file.Length, IsDirectory: false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that cannot be read cannot be sized, and one unreadable file in a
                    // folder of thousands is not a reason to fail the scan. It is left out of the
                    // total rather than counted as zero, so the number shown stays true to what
                    // was actually measured.
                }
            }

            files.Sort((left, right) => right.SizeBytes.CompareTo(left.SizeBytes));

            foreach (var file in files.Skip(MaxFileNodesPerFolder))
            {
                aggregateBytes += file.SizeBytes;
                aggregateCount++;
            }

            children.AddRange(files.Take(MaxFileNodesPerFolder));

            if (aggregateCount > 0)
            {
                children.Add(new UsageNode($"{AggregateNodeKey}:{aggregateCount}", folder, aggregateBytes, IsDirectory: false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadableFolders++;
        }

        // Then subfolders, each measured the same way.
        try
        {
            foreach (var subdirectory in directory.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Junctions and symlinks point at space that belongs to somewhere else; counting
                    // them here would double-count it and could loop forever.
                    if (subdirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadableFolders++;
                    continue;
                }

                var child = Measure(subdirectory.FullName, ref filesExamined, ref unreadableFolders, progress, cancellationToken);
                if (child.SizeBytes > 0)
                {
                    children.Add(child);
                    total += child.SizeBytes;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadableFolders++;
        }

        children.Sort((left, right) => right.SizeBytes.CompareTo(left.SizeBytes));

        return new UsageNode(name, folder, total, IsDirectory: true) { Children = children };
    }
}
