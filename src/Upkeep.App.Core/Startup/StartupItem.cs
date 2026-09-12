using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Startup;

/// <summary>Where Windows starts something from. Each one is toggled differently.</summary>
public enum StartupSource
{
    /// <summary>A Run value in the signed-in user's hive.</summary>
    RunKeyCurrentUser,

    /// <summary>A Run value for every account — changing it needs the elevated helper.</summary>
    RunKeyAllUsers,

    /// <summary>A RunOnce value: it runs at the next sign-in and then deletes itself.</summary>
    RunOnce,

    /// <summary>A shortcut in a Startup folder.</summary>
    StartupFolder,

    /// <summary>A scheduled task with a logon trigger.</summary>
    ScheduledTask,
}

/// <summary>
/// One thing that starts with Windows.
/// <para>
/// Turning an item off writes the same StartupApproved value Task Manager writes, so the two tools
/// agree and nothing is lost: a disabled item can always be turned back on. RunOnce entries are the
/// exception — they delete themselves after running, so the only thing to offer is removal.
/// </para>
/// </summary>
public sealed record StartupItem
{
    /// <summary>Stable identity: the registry value name, the shortcut file name, or the task path.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Who signed the target, where it could be read.</summary>
    public string? Publisher { get; init; }

    /// <summary>The command Windows runs, shown so the user can recognize what this actually is.</summary>
    public string? Command { get; init; }

    public required StartupSource Source { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>When it was turned off, if Windows recorded it.</summary>
    public DateTimeOffset? DisabledAt { get; init; }

    /// <summary>Machine-wide items need the elevated helper to change.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>Hive and key for registry-backed items, so a toggle knows where to write.</summary>
    public RegistryHiveName Hive { get; init; }

    public string? RegistryKeyPath { get; init; }

    /// <summary>Full task path for scheduled tasks (for example <c>\GoogleUpdateTaskMachine</c>).</summary>
    public string? TaskPath { get; init; }

    /// <summary>RunOnce entries can only be removed; everything else can be switched back on.</summary>
    public bool CanToggle => Source != StartupSource.RunOnce;
}
