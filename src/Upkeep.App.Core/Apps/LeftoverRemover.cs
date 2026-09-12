using System.Text.Json;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Apps;

/// <summary>What removing a set of leftovers actually achieved.</summary>
public sealed record LeftoverRemovalOutcome(string SessionId, int RemovedCount, int FailedCount, long QuarantinedBytes);

/// <summary>Removes the remnants a user approved after an uninstall.</summary>
public interface ILeftoverRemover
{
    Task<LeftoverRemovalOutcome> RemoveAsync(
        InstalledApp app,
        IReadOnlyList<LeftoverItem> items,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Clears approved leftovers, keeping every one of them recoverable.
/// <para>
/// Folders move to quarantine rather than being deleted; registry keys are exported to a backup
/// file first, so History can put them back. Both are journaled before they happen (ADR-0006).
/// Machine-wide items are refused here — those need the elevated helper, and this class runs in
/// the shell.
/// </para>
/// </summary>
public sealed class LeftoverRemover : ILeftoverRemover
{
    private static readonly JsonSerializerOptions BackupOptions = new() { WriteIndented = true };

    private readonly IQuarantineStore _quarantine;
    private readonly ISessionJournal _journal;
    private readonly IRegistryProbe _registry;
    private readonly RegistryKeyBackupService _backups;
    private readonly IWellKnownPaths _paths;
    private readonly IAppLogger _logger;

    public LeftoverRemover(
        IQuarantineStore quarantine,
        ISessionJournal journal,
        IRegistryProbe registry,
        IWellKnownPaths paths,
        IAppLogger logger)
    {
        _quarantine = quarantine;
        _journal = journal;
        _registry = registry;
        _backups = new RegistryKeyBackupService(registry);
        _paths = paths;
        _logger = logger;
    }

    public async Task<LeftoverRemovalOutcome> RemoveAsync(
        InstalledApp app,
        IReadOnlyList<LeftoverItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(items);

        var session = await _journal.StartAsync(SessionKind.Apps, cancellationToken);

        // The uninstall itself can't be undone, and History should say so rather than implying the
        // whole session is reversible.
        session = await _journal.AppendAsync(
            session,
            new IrreversibleOperationEntry("Apps.Uninstall", app.DisplayName) { Completed = true },
            cancellationToken);

        int removed = 0;
        int failed = 0;
        long quarantined = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.RequiresElevation)
            {
                // The shell has no business trying; the helper owns machine-wide removal.
                await _logger.LogWarningAsync($"Skipping machine-wide leftover {item.Path}: needs the elevated helper.", cancellationToken);
                failed++;
                continue;
            }

            (session, bool success, long bytes) = item.Kind switch
            {
                LeftoverKind.Folder => await RemoveFolderAsync(session, item, cancellationToken),
                _ => await RemoveRegistryKeyAsync(session, item, cancellationToken),
            };

            if (success)
            {
                removed++;
                quarantined += bytes;
            }
            else
            {
                failed++;
            }
        }

        await _journal.SaveAsync(session with { CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);

        return new LeftoverRemovalOutcome(session.Id, removed, failed, quarantined);
    }

    /// <summary>
    /// A leftover folder goes to quarantine whole. Files are moved one at a time because that is
    /// what the quarantine store records — and what History needs to put each one back.
    /// </summary>
    private async Task<(SessionManifest Session, bool Success, long Bytes)> RemoveFolderAsync(
        SessionManifest session,
        LeftoverItem item,
        CancellationToken cancellationToken)
    {
        long moved = 0;
        bool anyFailed = false;

        string[] files;
        try
        {
            files = Directory.Exists(item.Path)
                ? Directory.GetFiles(item.Path, "*", SearchOption.AllDirectories)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogWarningAsync($"Could not read leftover folder {item.Path}: {ex.Message}", cancellationToken);
            return (session, false, 0);
        }

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size = TryGetSize(file);
            session = await _journal.AppendAsync(session, new FileQuarantinedEntry(file, string.Empty, size), cancellationToken);
            int entryIndex = session.Entries.Count - 1;

            var result = await _quarantine.QuarantineAsync(file, session.Id, cancellationToken);
            anyFailed |= !result.Success;
            if (result.Success)
            {
                moved += size;
            }

            session = await _journal.UpdateEntryAsync(
                session,
                entryIndex,
                new FileQuarantinedEntry(file, result.QuarantinePath ?? string.Empty, size)
                {
                    Completed = result.Success,
                    FailureDetail = result.FailureDetail,
                },
                cancellationToken);
        }

        TryRemoveEmptyDirectory(item.Path);

        return (session, !anyFailed, moved);
    }

    /// <summary>
    /// Registry keys are exported before they are removed, so a revert has something to restore
    /// from. The export lives beside the session that made it.
    /// </summary>
    private async Task<(SessionManifest Session, bool Success, long Bytes)> RemoveRegistryKeyAsync(
        SessionManifest session,
        LeftoverItem item,
        CancellationToken cancellationToken)
    {
        if (!_registry.KeyExists(item.Hive, item.Path))
        {
            return (session, false, 0);
        }

        string backupPath = Path.Combine(
            _paths.LocalAppData,
            "Upkeep",
            "Sessions",
            $"{session.Id}-{Guid.NewGuid().ToString("N")[..8]}.regbackup.json");

        session = await _journal.AppendAsync(
            session,
            new RegistryKeyRemovedEntry(item.Hive.ToString(), item.Path, backupPath),
            cancellationToken);
        int entryIndex = session.Entries.Count - 1;

        bool success;
        string? failure = null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);

            // Captured with its values and everything under it, not just its subkey names: a
            // backup that cannot put the key back would make the entry's reversible claim untrue.
            var backup = _backups.Capture(item.Hive, item.Path);

            if (backup is null)
            {
                // The key went away between the check and the capture; there is nothing to
                // remove and nothing worth claiming was removed.
                success = false;
                failure = $"The key {item.Path} could not be read.";
            }
            else
            {
                await File.WriteAllTextAsync(backupPath, JsonSerializer.Serialize(backup, BackupOptions), cancellationToken);

                // Deleting the key itself is the elevated helper's job for HKLM; for HKCU the
                // shell does it, and the backup above is what makes that safe to offer.
                success = _registry.DeleteCurrentUserKeyTree(item.Path, out failure);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            success = false;
            failure = ex.Message;
        }

        session = await _journal.UpdateEntryAsync(
            session,
            entryIndex,
            new RegistryKeyRemovedEntry(item.Hive.ToString(), item.Path, backupPath)
            {
                Completed = success,
                FailureDetail = failure,
            },
            cancellationToken);

        return (session, success, 0);
    }

    private static long TryGetSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryRemoveEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind is untidy, not harmful.
        }
    }
}

/// <summary>What was under a registry key when Upkeep removed it.</summary>
