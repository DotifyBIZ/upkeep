using System.Text.Json.Serialization;

namespace Upkeep.App.Core.Sessions;

/// <summary>
/// One thing Upkeep did, with whatever it takes to put it back. Entries are written to disk
/// *before* the change is made (ADR-0006), so a session interrupted by a crash, a power cut or a
/// closed lid is still revertable.
/// <para>
/// The discriminators are persisted data: renaming one orphans the history of every past session
/// on a user's machine.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(FileQuarantinedEntry), "file-quarantined")]
[JsonDerivedType(typeof(FileDeletedEntry), "file-deleted")]
[JsonDerivedType(typeof(RegistryValueChangedEntry), "registry-value-changed")]
[JsonDerivedType(typeof(RegistryKeyRemovedEntry), "registry-key-removed")]
[JsonDerivedType(typeof(ServiceStartTypeChangedEntry), "service-start-type-changed")]
[JsonDerivedType(typeof(StartupItemToggledEntry), "startup-item-toggled")]
[JsonDerivedType(typeof(ScheduledTaskToggledEntry), "scheduled-task-toggled")]
[JsonDerivedType(typeof(SystemSettingChangedEntry), "system-setting-changed")]
[JsonDerivedType(typeof(IrreversibleOperationEntry), "irreversible")]
public abstract record SessionEntry
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Set once the action actually ran. An entry written but never completed is how an
    /// interrupted session shows up on the next launch.</summary>
    public bool Completed { get; init; }

    /// <summary>Populated when the action failed, so History can show what didn't happen.</summary>
    public string? FailureDetail { get; init; }

    /// <summary>Whether reverting this entry can restore anything at all.</summary>
    [JsonIgnore]
    public abstract bool IsReversible { get; }

    /// <summary>Bytes this action freed, for the results figure.</summary>
    [JsonIgnore]
    public virtual long FreedBytes => 0;
}

/// <summary>A file moved to quarantine — restorable until the retention period runs out.</summary>
public sealed record FileQuarantinedEntry(string OriginalPath, string QuarantinePath, long SizeBytes) : SessionEntry
{
    public override bool IsReversible => true;

    // The space isn't reclaimed until the quarantine is purged, so this deliberately reports zero:
    // the results screen counts it separately as "will be freed" rather than as freed.
    public override long FreedBytes => 0;
}

/// <summary>A file deleted outright — only ever a cache or temporary file (ADR-0006).</summary>
public sealed record FileDeletedEntry(string Path, long SizeBytes) : SessionEntry
{
    public override bool IsReversible => false;

    public override long FreedBytes => SizeBytes;
}

/// <summary>A registry value changed or removed, with its previous content for the way back.</summary>
public sealed record RegistryValueChangedEntry(
    string Hive,
    string KeyPath,
    string ValueName,
    string? PreviousValueKind,
    string? PreviousValue) : SessionEntry
{
    public override bool IsReversible => true;
}

/// <summary>A registry key removed after an uninstall, exported first so it can be put back.</summary>
public sealed record RegistryKeyRemovedEntry(string Hive, string KeyPath, string BackupPath) : SessionEntry
{
    public override bool IsReversible => true;
}

/// <summary>A service's start type changed; the previous type is what a revert restores.</summary>
public sealed record ServiceStartTypeChangedEntry(string ServiceName, string PreviousStartType, string NewStartType) : SessionEntry
{
    public override bool IsReversible => true;
}

/// <summary>A startup item enabled or disabled, the way Task Manager does it.</summary>
public sealed record StartupItemToggledEntry(string ItemId, string Source, bool PreviouslyEnabled) : SessionEntry
{
    public override bool IsReversible => true;
}

/// <summary>A logon task enabled or disabled.</summary>
public sealed record ScheduledTaskToggledEntry(string TaskPath, bool PreviouslyEnabled) : SessionEntry
{
    public override bool IsReversible => true;
}

/// <summary>A Windows setting Upkeep can put back (visual effects, power plan, indexing).</summary>
public sealed record SystemSettingChangedEntry(string SettingId, string PreviousValue, string NewValue) : SessionEntry
{
    public override bool IsReversible => true;
}

/// <summary>
/// Something Windows did that has no way back — emptying the Recycle Bin, component store cleanup,
/// an app's own uninstaller. Recorded so History can say what happened, and say plainly that it
/// can't be undone.
/// </summary>
public sealed record IrreversibleOperationEntry(string OperationKey, string Description, long FreedBytesEstimate = 0) : SessionEntry
{
    public override bool IsReversible => false;

    public override long FreedBytes => FreedBytesEstimate;
}
