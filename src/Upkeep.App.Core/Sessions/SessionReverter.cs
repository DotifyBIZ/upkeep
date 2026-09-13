using System.Text.Json;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Startup;
using Upkeep.App.Core.Updates;

namespace Upkeep.App.Core.Sessions;

/// <summary>What a revert managed to put back.</summary>
/// <param name="RevertedCount">Entries actually undone.</param>
/// <param name="FailedCount">Entries that could not be undone, despite being reversible.</param>
/// <param name="SkippedCount">Entries there was never a way back from — a deleted cache file, an
/// app's own uninstaller — plus any that never completed in the first place.</param>
public sealed record RevertOutcome(int RevertedCount, int FailedCount, int SkippedCount)
{
    public bool AnythingReverted => RevertedCount > 0;
}

/// <summary>Puts a whole session back, as far as anything in it can be put back.</summary>
public interface ISessionReverter
{
    Task<(SessionManifest Session, RevertOutcome Outcome)> RevertAsync(
        SessionManifest session,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The one-click revert behind the History page (ADR-0006).
/// <para>
/// Entries are undone newest first, so a session that moved a folder and then changed a setting
/// unwinds in the order it was wound. Nothing here writes new journal entries: a revert undoes a
/// session rather than adding to it, which is why it uses the non-journaling paths and stamps
/// <see cref="SessionManifest.RevertedAt"/> at the end.
/// </para>
/// <para>
/// An entry that was never completed, or that was never reversible, is skipped rather than
/// attempted — History says plainly which of those a session contains.
/// </para>
/// </summary>
public sealed class SessionReverter : ISessionReverter
{
    private readonly IQuarantineStore _quarantine;
    private readonly RegistryKeyBackupService _backups;
    private readonly IElevationService _elevation;
    private readonly IStartupItemScanner _startupScanner;
    private readonly IStartupItemToggler _startupToggler;
    private readonly IPerformanceSettings _performance;
    private readonly ISessionJournal _journal;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public SessionReverter(
        IQuarantineStore quarantine,
        RegistryKeyBackupService backups,
        IElevationService elevation,
        IStartupItemScanner startupScanner,
        IStartupItemToggler startupToggler,
        IPerformanceSettings performance,
        ISessionJournal journal,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        _quarantine = quarantine;
        _backups = backups;
        _elevation = elevation;
        _startupScanner = startupScanner;
        _startupToggler = startupToggler;
        _performance = performance;
        _journal = journal;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<(SessionManifest Session, RevertOutcome Outcome)> RevertAsync(
        SessionManifest session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.CanRevert)
        {
            return (session, new RevertOutcome(0, 0, session.Entries.Count));
        }

        int reverted = 0;
        int failed = 0;
        int skipped = 0;

        // Scanning startup items is the expensive part, and most sessions have none, so it happens
        // on first need rather than on every revert.
        IReadOnlyList<StartupItem>? startupItems = null;

        // Newest first: a session unwinds in the reverse of the order it was wound.
        for (int index = session.Entries.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = session.Entries[index];
            if (!entry.Completed || !entry.IsReversible)
            {
                skipped++;
                continue;
            }

            bool success;
            switch (entry)
            {
                case FileQuarantinedEntry file:
                    success = await _quarantine.RestoreAsync(file.QuarantinePath, file.OriginalPath, cancellationToken);
                    break;

                case RegistryKeyRemovedEntry registry:
                    success = await RestoreRegistryKeyAsync(registry, cancellationToken);
                    break;

                case ServiceStartTypeChangedEntry service:
                    success = await RestoreServiceAsync(service, cancellationToken);
                    break;

                case StartupItemToggledEntry startup:
                    startupItems ??= await _startupScanner.ScanAsync(cancellationToken);
                    success = await RestoreStartupItemAsync(startup, startupItems, cancellationToken);
                    break;

                case SystemSettingChangedEntry setting:
                    success = await RestoreSystemSettingAsync(setting, cancellationToken);
                    break;

                default:
                    // A reversible entry this build doesn't know how to undo is skipped rather
                    // than counted as reverted — History must not claim more than happened.
                    await _logger.LogWarningAsync($"Revert skipped an entry it does not handle: {entry.GetType().Name}.", cancellationToken);
                    skipped++;
                    continue;
            }

            if (success)
            {
                reverted++;
            }
            else
            {
                failed++;
            }
        }

        // Stamped even when some entries failed: the session has been through a revert, and
        // offering the whole thing again would repeat what already worked.
        session = await _journal.SaveAsync(session with { RevertedAt = DateTimeOffset.UtcNow }, cancellationToken);

        return (session, new RevertOutcome(reverted, failed, skipped));
    }

    private async Task<bool> RestoreRegistryKeyAsync(RegistryKeyRemovedEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(entry.BackupPath))
            {
                await _logger.LogWarningAsync($"The backup for {entry.KeyPath} is no longer on disk.", cancellationToken);
                return false;
            }

            string json = await File.ReadAllTextAsync(entry.BackupPath, cancellationToken);
            var backup = JsonSerializer.Deserialize<RegistryKeyBackup>(json);

            if (backup is null)
            {
                return false;
            }

            if (_backups.Restore(backup, out string? failure))
            {
                return true;
            }

            await _logger.LogWarningAsync($"Putting {entry.KeyPath} back failed: {failure}", cancellationToken);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            await _logger.LogErrorAsync($"Reading the backup for {entry.KeyPath} failed.", ex, cancellationToken);
            return false;
        }
    }

    private async Task<bool> RestoreServiceAsync(ServiceStartTypeChangedEntry entry, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(entry.PreviousStartType, ignoreCase: true, out ServiceStartType previous))
        {
            await _logger.LogWarningAsync($"The recorded start type for {entry.ServiceName} is not one Upkeep sets: {entry.PreviousStartType}.", cancellationToken);
            return false;
        }

        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            return false;
        }

        // Back through the helper, which classifies the service again before acting — a revert is
        // not a reason to bypass the boundary (ADR-0005).
        var response = await _elevation.SendAsync(new SetServiceStartTypeRequest(entry.ServiceName, previous), cancellationToken);
        return response is ServiceChangeResponse { Success: true };
    }

    private async Task<bool> RestoreStartupItemAsync(
        StartupItemToggledEntry entry,
        IReadOnlyList<StartupItem> startupItems,
        CancellationToken cancellationToken)
    {
        // Matched against a fresh scan rather than rebuilt from the entry: the approved value lives
        // under Run, Run32 or StartupFolder depending on the item, and only the real item says which.
        var item = startupItems.FirstOrDefault(candidate =>
            candidate.Id.Equals(entry.ItemId, StringComparison.OrdinalIgnoreCase)
            && candidate.Source.ToString().Equals(entry.Source, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            await _logger.LogWarningAsync($"The startup item {entry.ItemId} is no longer on this machine.", cancellationToken);
            return false;
        }

        (bool success, string? failure) = await _startupToggler.SetEnabledWithoutJournalAsync(item, entry.PreviouslyEnabled, cancellationToken);

        if (!success)
        {
            await _logger.LogWarningAsync($"Putting {entry.ItemId} back failed: {failure}", cancellationToken);
        }

        return success;
    }

    /// <summary>
    /// Puts one Windows setting back. The ids are the ones the Performance and Drivers tabs write;
    /// anything else is refused rather than guessed at.
    /// </summary>
    private async Task<bool> RestoreSystemSettingAsync(SystemSettingChangedEntry entry, CancellationToken cancellationToken)
    {
        switch (entry.SettingId)
        {
            case PerformanceSettingIds.Animations:
                return bool.TryParse(entry.PreviousValue, out bool animations)
                    && _performance.SetAnimationsEnabled(animations, out _);

            case PerformanceSettingIds.Transparency:
                return bool.TryParse(entry.PreviousValue, out bool transparency)
                    && _performance.SetTransparencyEnabled(transparency, out _);

            case PerformanceSettingIds.PowerPlan:
                return Guid.TryParse(entry.PreviousValue, out Guid planId)
                    && _performance.SetActivePowerPlan(planId, out _);

            case UpdateSettingIds.Pause:
                return await RestoreUpdatePauseAsync(entry, cancellationToken);

            case UpdateSettingIds.Deferral:
                return WindowsUpdatePolicy.TryParseDeferral(entry.PreviousValue, out int featureDays, out int qualityDays)
                    && await SendUpdateChangeAsync(new SetUpdateDeferralRequest(featureDays, qualityDays), cancellationToken);

            default:
                return false;
        }
    }

    /// <summary>
    /// Puts a Windows Update pause back. The entry records when the old pause was due to end, so
    /// what comes back is the days it had left — rounded up to whole days, because that is the
    /// resolution the helper request has. A pause that had already lapsed comes back as a resume.
    /// </summary>
    private Task<bool> RestoreUpdatePauseAsync(SystemSettingChangedEntry entry, CancellationToken cancellationToken)
    {
        var expiry = WindowsUpdatePolicy.ParseTime(entry.PreviousValue);
        int days = WindowsUpdatePolicy.PauseDaysRemaining(expiry, _timeProvider.GetUtcNow());

        return SendUpdateChangeAsync(new SetUpdatePauseRequest(days), cancellationToken);
    }

    /// <summary>
    /// Back through the helper, which is the only thing on this machine that may write these keys
    /// (ADR-0005). A revert is not a reason to bypass the boundary.
    /// </summary>
    private async Task<bool> SendUpdateChangeAsync(HelperRequest request, CancellationToken cancellationToken)
    {
        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            return false;
        }

        var response = await _elevation.SendAsync(request, cancellationToken);
        return response is WindowsUpdateStateResponse { Success: true };
    }
}


/// <summary>
/// The setting ids written into <see cref="SystemSettingChangedEntry"/>. They are on-disk data, so
/// they are constants in one place rather than string literals at both ends.
/// </summary>
public static class PerformanceSettingIds
{
    public const string Animations = "visual-effects-animations";

    public const string Transparency = "visual-effects-transparency";

    public const string PowerPlan = "power-plan";
}

/// <summary>
/// The setting ids the Drivers tab writes. Windows Update's schedule is machine-wide, so putting
/// one back goes through the elevated helper rather than straight at the registry.
/// </summary>
public static class UpdateSettingIds
{
    public const string Pause = "windows-update-pause";

    public const string Deferral = "windows-update-deferral";
}
