using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Files;

/// <summary>What the user asked to happen to a set of files they picked.</summary>
/// <param name="Paths">Exactly what the preview listed — nothing is re-derived or expanded here.</param>
/// <param name="DeletePermanently">
/// The explicit per-action opt-out from quarantine (ADR-0006). Off by default everywhere in the UI.
/// </param>
public sealed record FileActionRequest(IReadOnlyList<string> Paths, bool DeletePermanently = false);

/// <summary>What actually happened to them.</summary>
public sealed record FileActionOutcome(
    string SessionId,
    long AffectedBytes,
    int RemovedCount,
    int FailedCount,
    bool WasQuarantined)
{
    /// <summary>
    /// Space back on the drive right now. Quarantined files still occupy it until the quarantine is
    /// purged, so reporting them as freed would be the "7.7 GB freed" that isn't.
    /// </summary>
    public long FreedBytes => WasQuarantined ? 0 : AffectedBytes;
}

/// <summary>Removes files the user selected on the Files page.</summary>
public interface IFileActionExecutor
{
    Task<FileActionOutcome> RemoveAsync(FileActionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Moves chosen files to quarantine — or, only when explicitly asked, deletes them outright.
/// <para>
/// Every file is written to the session journal *before* anything happens to it, so a run
/// interrupted by a crash or a closed lid still leaves a record of what it had started
/// (ADR-0006). One failure never stops the rest: a locked file is a number on the results screen,
/// not an aborted run.
/// </para>
/// </summary>
public sealed class FileActionExecutor : IFileActionExecutor
{
    private readonly IQuarantineStore _quarantine;
    private readonly ISessionJournal _journal;
    private readonly IAppLogger _logger;

    public FileActionExecutor(IQuarantineStore quarantine, ISessionJournal journal, IAppLogger logger)
    {
        _quarantine = quarantine;
        _journal = journal;
        _logger = logger;
    }

    public async Task<FileActionOutcome> RemoveAsync(FileActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await _journal.StartAsync(SessionKind.Files, cancellationToken);
        long affected = 0;
        int removed = 0;
        int failed = 0;

        foreach (string path in request.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size = TryGetSize(path);
            if (size < 0)
            {
                failed++;
                continue;
            }

            // Recorded before the file is touched. The quarantine path isn't known yet, which is
            // exactly why the entry is completed in a second write rather than one.
            SessionEntry entry = request.DeletePermanently
                ? new FileDeletedEntry(path, size)
                : new FileQuarantinedEntry(path, string.Empty, size);

            session = await _journal.AppendAsync(session, entry, cancellationToken);
            int entryIndex = session.Entries.Count - 1;

            (bool success, string? quarantinePath, string? failure) = request.DeletePermanently
                ? DeletePermanently(path)
                : await QuarantineAsync(path, session.Id, cancellationToken);

            if (success)
            {
                affected += size;
                removed++;
            }
            else
            {
                failed++;
                await _logger.LogWarningAsync($"Could not remove {path}: {failure}", cancellationToken);
            }

            SessionEntry completed = request.DeletePermanently
                ? new FileDeletedEntry(path, size) { Completed = success, FailureDetail = failure }
                : new FileQuarantinedEntry(path, quarantinePath ?? string.Empty, size) { Completed = success, FailureDetail = failure };

            session = await _journal.UpdateEntryAsync(session, entryIndex, completed, cancellationToken);
        }

        await _journal.SaveAsync(session with { CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);

        return new FileActionOutcome(session.Id, affected, removed, failed, !request.DeletePermanently);
    }

    private async Task<(bool Success, string? QuarantinePath, string? Failure)> QuarantineAsync(string path, string sessionId, CancellationToken cancellationToken)
    {
        var result = await _quarantine.QuarantineAsync(path, sessionId, cancellationToken);
        return (result.Success, result.QuarantinePath, result.FailureDetail);
    }

    private static (bool Success, string? QuarantinePath, string? Failure) DeletePermanently(string path)
    {
        try
        {
            File.Delete(path);
            return (true, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>Size now, not the size the scan saw. Returns -1 when the file is already gone.</summary>
    private static long TryGetSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return -1;
        }
    }
}
