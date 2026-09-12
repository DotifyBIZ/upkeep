namespace Upkeep.App.Core.Files;

/// <summary>Thresholds for what counts as worth surfacing. Both are the user's to set.</summary>
public sealed record LargeFileCriteria
{
    /// <summary>Files at least this big qualify on size alone.</summary>
    public long MinimumBytes { get; init; } = 250L * 1024 * 1024;

    /// <summary>Files untouched for at least this long qualify on age, whatever their size (above
    /// the scan's own floor).</summary>
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromDays(180);

    /// <summary>Whether age alone is enough, or a file has to be both big and old.</summary>
    public bool RequireBoth { get; init; }
}

/// <summary>One file worth a second look, and why it showed up.</summary>
public sealed record LargeFileResult(ScannedFile File, bool IsLarge, bool IsOld)
{
    public long SizeBytes => File.SizeBytes;
}

/// <summary>Surfaces oversized and forgotten files.</summary>
public interface ILargeFileFinder
{
    Task<IReadOnlyList<LargeFileResult>> FindAsync(
        FileScanScope scope,
        LargeFileCriteria criteria,
        IProgress<FileScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The simplest of the three Files scans: no hashing, no tree building — just the files that are
/// unusually big or haven't been opened in months, sorted so the biggest wins are at the top.
/// Nothing here decides anything is safe to delete; it reports, and the user chooses.
/// </summary>
public sealed class LargeFileFinder : ILargeFileFinder
{
    private readonly TimeProvider _timeProvider;

    public LargeFileFinder(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<LargeFileResult>> FindAsync(
        FileScanScope scope,
        LargeFileCriteria criteria,
        IProgress<FileScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(criteria);

        return Task.Run<IReadOnlyList<LargeFileResult>>(() =>
        {
            var files = FileEnumerator.Enumerate(scope, out _, progress, cancellationToken);
            var cutoff = _timeProvider.GetUtcNow().UtcDateTime - criteria.MinimumAge;

            var results = new List<LargeFileResult>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool isLarge = file.SizeBytes >= criteria.MinimumBytes;
                bool isOld = file.LastWriteUtc <= cutoff;

                bool qualifies = criteria.RequireBoth ? isLarge && isOld : isLarge || isOld;
                if (qualifies)
                {
                    results.Add(new LargeFileResult(file, isLarge, isOld));
                }
            }

            return [.. results.OrderByDescending(result => result.SizeBytes)];
        }, cancellationToken);
    }
}
