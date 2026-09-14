using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Quarantine;

/// <summary>Outcome of moving a file into quarantine.</summary>
public sealed record QuarantineResult(bool Success, string? QuarantinePath, string? FailureDetail)
{
    public static QuarantineResult Failed(string detail) => new(false, null, detail);
}

/// <summary>
/// Holds files the user might want back — duplicates, large files, uninstall leftovers — for the
/// retention period, then deletes them (ADR-0006). Caches and temp files never come here: keeping
/// a copy of a cache would mean a cleanup frees nothing.
/// </summary>
public interface IQuarantineStore
{
    /// <summary>Moves a file in, returning where it went so the session journal can record it.</summary>
    Task<QuarantineResult> QuarantineAsync(string path, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Puts a quarantined file back where it came from.</summary>
    Task<bool> RestoreAsync(string quarantinePath, string originalPath, CancellationToken cancellationToken = default);

    /// <summary>Total size currently held.</summary>
    long GetTotalBytes();

    /// <summary>Deletes everything older than <paramref name="retention"/>. Returns bytes freed.</summary>
    long Purge(TimeSpan retention, DateTimeOffset now);

    /// <summary>Deletes everything, now — the "Empty now" button in Settings.</summary>
    long PurgeAll();
}

/// <inheritdoc />
public sealed class QuarantineStore : IQuarantineStore
{
    /// <summary>Folder name used at the root of non-system volumes, so a move stays a rename.</summary>
    public const string VolumeFolderName = ".upkeep-quarantine";

    private readonly IWellKnownPaths _paths;

    public QuarantineStore(IWellKnownPaths paths) => _paths = paths;

    /// <summary>Where files from the system volume are kept.</summary>
    public string PrimaryRoot => Path.Combine(_paths.LocalAppData, "Upkeep", "Quarantine");

    public async Task<QuarantineResult> QuarantineAsync(string path, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!File.Exists(path))
        {
            return QuarantineResult.Failed("The file no longer exists.");
        }

        try
        {
            string destination = BuildDestinationPath(path, sessionId);
            if (Path.GetDirectoryName(destination) is string destinationFolder)
            {
                Directory.CreateDirectory(destinationFolder);
            }

            // Same volume, so this is a rename: instant, and it never leaves a half-copied file
            // behind on a disk that just ran out of space.
            File.Move(path, destination, overwrite: false);

            await Task.CompletedTask;
            return new QuarantineResult(true, destination, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return QuarantineResult.Failed(ex.Message);
        }
    }

    public async Task<bool> RestoreAsync(string quarantinePath, string originalPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(quarantinePath))
            {
                return false;
            }

            string? directory = Path.GetDirectoryName(originalPath);
            if (directory is null)
            {
                return false;
            }

            Directory.CreateDirectory(directory);

            // Something may already occupy the original name — a reinstalled app, a re-downloaded
            // file. Restoring beside it is better than overwriting whatever is there now.
            string destination = File.Exists(originalPath) ? BuildRestoredAlongsidePath(originalPath) : originalPath;
            File.Move(quarantinePath, destination, overwrite: false);

            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public long GetTotalBytes() =>
        EnumerateRoots().Sum(root => DirectorySize(root));

    public long Purge(TimeSpan retention, DateTimeOffset now)
    {
        long freed = 0;
        var cutoff = now - retention;

        foreach (string root in EnumerateRoots())
        {
            foreach (string sessionFolder in SafeEnumerateDirectories(root))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(sessionFolder) > cutoff.UtcDateTime)
                    {
                        continue;
                    }

                    freed += DirectorySize(sessionFolder);
                    Directory.Delete(sessionFolder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A folder held open by something else is retried on the next purge.
                }
            }
        }

        return freed;
    }

    public long PurgeAll() => Purge(TimeSpan.Zero, DateTimeOffset.UtcNow);

    /// <summary>
    /// Quarantine lives on the same volume as the file it holds, so moving in and out is a rename
    /// rather than a copy — instant, and it can't half-succeed on a full disk.
    /// </summary>
    private string BuildDestinationPath(string path, string sessionId)
    {
        string? volumeRoot = Path.GetPathRoot(Path.GetFullPath(path));
        bool onSystemVolume = volumeRoot is not null
            && string.Equals(volumeRoot, Path.GetPathRoot(_paths.LocalAppData), StringComparison.OrdinalIgnoreCase);

        string root = onSystemVolume
            ? PrimaryRoot
            : Path.Combine(volumeRoot ?? PrimaryRoot, VolumeFolderName);

        // The original name is kept for the UI, prefixed so two files with the same name from
        // different folders can't collide inside one session.
        string fileName = $"{Guid.NewGuid().ToString("N")[..8]}-{Path.GetFileName(path)}";
        return Path.Combine(root, sessionId, fileName);
    }

    private static string BuildRestoredAlongsidePath(string originalPath)
    {
        // A file's original path always has a folder. If it somehow does not, the manifest is
        // malformed and there is nowhere to put the file back — say that, rather than guess.
        string directory = Path.GetDirectoryName(originalPath)
            ?? throw new ArgumentException("A quarantined file's original path names no folder to restore it into.", nameof(originalPath));
        string name = Path.GetFileNameWithoutExtension(originalPath);
        string extension = Path.GetExtension(originalPath);

        for (int suffix = 1; suffix < 1000; suffix++)
        {
            string candidate = Path.Combine(directory, $"{name} (restored {suffix}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name} (restored {Guid.NewGuid():N}){extension}");
    }

    private IEnumerable<string> EnumerateRoots()
    {
        yield return PrimaryRoot;

        foreach (var drive in DriveInfo.GetDrives())
        {
            string candidate;
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                {
                    continue;
                }

                candidate = Path.Combine(drive.RootDirectory.FullName, VolumeFolderName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
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

    private static long DirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            long total = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                total += file.Length;
            }

            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
