using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Startup;

/// <summary>Turns startup items on and off, and removes RunOnce entries.</summary>
public interface IStartupItemToggler
{
    /// <summary>
    /// Switches an item on or off, recording the previous state first so History can undo it.
    /// Returns the updated session.
    /// </summary>
    Task<(SessionManifest Session, bool Success)> SetEnabledAsync(
        SessionManifest session,
        StartupItem item,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches an item without writing a journal entry. This is the way back for a revert, which
    /// is undoing a session rather than adding to it.
    /// </summary>
    Task<(bool Success, string? Failure)> SetEnabledWithoutJournalAsync(
        StartupItem item,
        bool enabled,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes the same StartupApproved value Task Manager writes, and asks <c>schtasks</c> to enable or
/// disable a scheduled task.
/// <para>
/// Nothing is deleted to disable something: an item turned off keeps its Run entry, so turning it
/// back on is a single value away. Machine-wide items are refused here — those belong to the
/// elevated helper (ADR-0005).
/// </para>
/// </summary>
public sealed class StartupItemToggler : IStartupItemToggler
{
    private static readonly TimeSpan TaskChangeTimeout = TimeSpan.FromSeconds(30);

    private readonly IRegistryProbe _registry;
    private readonly IWindowsToolRunner _toolRunner;
    private readonly IWellKnownPaths _paths;
    private readonly ISessionJournal _journal;
    private readonly TimeProvider _timeProvider;

    public StartupItemToggler(
        IRegistryProbe registry,
        IWindowsToolRunner toolRunner,
        IWellKnownPaths paths,
        ISessionJournal journal,
        TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _toolRunner = toolRunner;
        _paths = paths;
        _journal = journal;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<(SessionManifest Session, bool Success)> SetEnabledAsync(
        SessionManifest session,
        StartupItem item,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);

        if (!item.CanToggle)
        {
            // A RunOnce entry has already been consumed or will be; toggling it means nothing.
            return (session, false);
        }

        // Journaled before the change, with the state to go back to (ADR-0006).
        session = await _journal.AppendAsync(
            session,
            new StartupItemToggledEntry(item.Id, item.Source.ToString(), item.IsEnabled),
            cancellationToken);
        int entryIndex = session.Entries.Count - 1;

        (bool success, string? failure) = await SetEnabledWithoutJournalAsync(item, enabled, cancellationToken);

        session = await _journal.UpdateEntryAsync(
            session,
            entryIndex,
            new StartupItemToggledEntry(item.Id, item.Source.ToString(), item.IsEnabled)
            {
                Completed = success,
                FailureDetail = failure,
            },
            cancellationToken);

        return (session, success);
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Failure)> SetEnabledWithoutJournalAsync(
        StartupItem item,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.CanToggle)
        {
            return (false, "This entry cannot be toggled.");
        }

        return item.Source == StartupSource.ScheduledTask
            ? await SetTaskEnabledAsync(item, enabled, cancellationToken)
            : SetApprovedValue(item, enabled);
    }

    /// <summary>
    /// The StartupApproved value lives in the current user's hive even for machine-wide Run
    /// entries, which is what lets one user disable a shared startup item without administrator
    /// rights — exactly what Task Manager does.
    /// </summary>
    private (bool Success, string? Failure) SetApprovedValue(StartupItem item, bool enabled)
    {
        string approvedKey = ApprovedKeyFor(item);
        byte[] value = enabled
            ? StartupApprovedValue.Enabled()
            : StartupApprovedValue.Disabled(_timeProvider.GetUtcNow());

        bool success = _registry.SetCurrentUserBinaryValue(approvedKey, item.Id, value, out string? failure);
        return (success, failure);
    }

    private static string ApprovedKeyFor(StartupItem item) => item.Source switch
    {
        StartupSource.StartupFolder => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
        _ when item.RegistryKeyPath?.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) == true =>
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
        _ => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
    };

    private async Task<(bool Success, string? Failure)> SetTaskEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.TaskPath))
        {
            return (false, "This task records no path.");
        }

        string schtasks = Path.Combine(_paths.WindowsDirectory, "System32", "schtasks.exe");
        var result = await _toolRunner.RunAsync(
            schtasks,
            ["/change", "/tn", item.TaskPath, enabled ? "/enable" : "/disable"],
            TaskChangeTimeout,
            cancellationToken);

        // schtasks prints in the user's language, so only the exit code is read (CLAUDE.md).
        return (result.Succeeded, result.Succeeded ? null : $"schtasks exited with {result.ExitCode}");
    }
}
