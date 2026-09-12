using System.Security.Cryptography;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Files;

/// <summary>Finds files with identical content.</summary>
public interface IDuplicateFinder
{
    Task<DuplicateScanResult> FindAsync(FileScanScope scope, IProgress<FileScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Matches by content, never by name (README, ADR-0006): a delete action based on a filename
/// guess is exactly the mistake this product can't afford.
/// <para>
/// Three passes, cheapest first — group by size, compare a leading chunk, then hash in full — so a
/// full drive doesn't get hashed to find the handful of files that actually collide. Hard links are
/// collapsed before anything is reported: they are byte-identical by definition, but deleting one
/// frees nothing, and offering that as reclaimable space would be a lie measured in gigabytes.
/// </para>
/// </summary>
public sealed class DuplicateFinder : IDuplicateFinder
{
    /// <summary>How much of a file to compare before committing to hashing all of it.</summary>
    private const int PartialHashBytes = 64 * 1024;

    private readonly IFileIdentityReader _identityReader;

    public DuplicateFinder(IFileIdentityReader identityReader) => _identityReader = identityReader;

    public Task<DuplicateScanResult> FindAsync(
        FileScanScope scope,
        IProgress<FileScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // Hashing is I/O-bound but long-running; keep it off the caller's thread entirely.
        return Task.Run(() => Find(scope, progress, cancellationToken), cancellationToken);
    }

    private DuplicateScanResult Find(FileScanScope scope, IProgress<FileScanProgress>? progress, CancellationToken cancellationToken)
    {
        var files = FileEnumerator.Enumerate(scope, out int skippedCloudFiles, progress, cancellationToken);
        var groups = new List<DuplicateGroup>();

        // Pass 1: same size. Files of different sizes cannot possibly have the same content, and
        // this alone removes almost everything on a normal disk.
        foreach (var sameSize in files.GroupBy(file => file.SizeBytes).Where(group => group.Count() > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Pass 2: the first chunk. Different files that happen to share a size almost always
            // differ within the first few kilobytes.
            foreach (var samePrefix in GroupBy(sameSize, file => ComputePartialHash(file.Path), cancellationToken))
            {
                // Pass 3: the whole file. Only now is the full read worth paying for.
                foreach (var identical in GroupBy(samePrefix, file => ComputeFullHash(file.Path), cancellationToken))
                {
                    var distinct = CollapseHardLinks(identical);
                    if (distinct.Count > 1)
                    {
                        groups.Add(BuildGroup(distinct));
                    }
                }
            }
        }

        return new DuplicateScanResult(
            [.. groups.OrderByDescending(group => group.ReclaimableBytes)],
            files.Count,
            skippedCloudFiles);
    }

    private static List<List<ScannedFile>> GroupBy(
        IEnumerable<ScannedFile> files,
        Func<ScannedFile, string?> keySelector,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, List<ScannedFile>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? key = keySelector(file);
            if (key is null)
            {
                // Unreadable right now — locked, or gone since enumeration. Left out rather than
                // guessed at.
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
            }

            group.Add(file);
        }

        return [.. groups.Values.Where(group => group.Count > 1)];
    }

    /// <summary>
    /// Reduces hard links to one entry each. Two paths pointing at the same bytes are not two
    /// copies, and the space "freed" by deleting one is zero.
    /// </summary>
    private List<ScannedFile> CollapseHardLinks(IReadOnlyList<ScannedFile> identical)
    {
        var seen = new HashSet<FileId>();
        var distinct = new List<ScannedFile>();

        foreach (var file in identical)
        {
            var id = _identityReader.TryRead(file.Path);
            if (id is null || seen.Add(id.Value))
            {
                distinct.Add(file);
            }
        }

        return distinct;
    }

    /// <summary>
    /// Keeps the oldest copy. The original is usually where the user filed it deliberately, and
    /// the newer ones are the accidents — a re-download, a copy to the desktop.
    /// </summary>
    private static DuplicateGroup BuildGroup(IReadOnlyList<ScannedFile> files)
    {
        var ordered = files.OrderBy(file => file.LastWriteUtc).ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
        return new DuplicateGroup(ordered, ordered[0]);
    }

    private static string? ComputePartialHash(string path) => ComputeHash(path, PartialHashBytes);

    private static string? ComputeFullHash(string path) => ComputeHash(path, limitBytes: null);

    private static string? ComputeHash(string path, int? limitBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();

            if (limitBytes is null)
            {
                return Convert.ToHexString(sha.ComputeHash(stream));
            }

            byte[] buffer = new byte[limitBytes.Value];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
