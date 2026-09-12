namespace Upkeep.App.Core.Platform;

/// <summary>
/// One registry value, captured well enough to put it back exactly as it was.
/// <para>
/// The kind is stored as a string rather than the enum so an unfamiliar kind written by a future
/// build reads back as data rather than failing to deserialize the whole backup.
/// </para>
/// </summary>
public sealed record RegistryValueSnapshot(
    string Name,
    string Kind,
    string? Text = null,
    IReadOnlyList<string>? Lines = null,
    byte[]? Binary = null,
    long? Number = null);

/// <summary>
/// A registry key and everything under it, as it was before Upkeep removed it.
/// <para>
/// Recursive on purpose: a backup that records only the names of subkeys cannot put anything back,
/// and an entry that claims to be reversible has to be.
/// </para>
/// </summary>
public sealed record RegistryKeyBackup(
    string Hive,
    string KeyPath,
    IReadOnlyList<RegistryValueSnapshot> Values,
    IReadOnlyList<RegistryKeyBackup> SubKeys);

/// <summary>
/// Captures a key tree before it is removed, and puts one back on revert.
/// <para>
/// The recursion lives here rather than in <see cref="RegistryProbe"/> so it can be tested against
/// a substitute registry — the probe itself is a thin wrapper with nothing worth testing, and
/// walking a key tree is not.
/// </para>
/// </summary>
public sealed class RegistryKeyBackupService
{
    /// <summary>
    /// How deep a capture will walk. Uninstall leftovers are shallow; the limit is here so a
    /// pathological key can't turn a backup into an unbounded walk.
    /// </summary>
    public const int MaxDepth = 32;

    private readonly IRegistryProbe _registry;

    public RegistryKeyBackupService(IRegistryProbe registry) => _registry = registry;

    /// <summary>Captures a key and everything under it, or null when the key isn't there.</summary>
    public RegistryKeyBackup? Capture(RegistryHiveName hive, string keyPath) => Capture(hive, keyPath, MaxDepth);

    /// <summary>
    /// Puts a captured key tree back, in the current user's hive only. Machine-wide keys are the
    /// elevated helper's business, and restoring one from the shell would be a hole in ADR-0005.
    /// </summary>
    public bool Restore(RegistryKeyBackup backup, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(backup);

        if (!string.Equals(backup.Hive, RegistryHiveName.CurrentUser.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            failure = $"Upkeep only restores keys in {RegistryHiveName.CurrentUser}.";
            return false;
        }

        return RestoreTree(backup, backup.KeyPath, out failure);
    }

    private RegistryKeyBackup? Capture(RegistryHiveName hive, string keyPath, int depthRemaining)
    {
        if (!_registry.KeyExists(hive, keyPath))
        {
            return null;
        }

        var values = _registry.GetValues(hive, keyPath);

        var subKeys = new List<RegistryKeyBackup>();
        if (depthRemaining > 0)
        {
            foreach (string name in _registry.GetSubKeyNames(hive, keyPath))
            {
                var child = Capture(hive, $@"{keyPath}\{name}", depthRemaining - 1);
                if (child is not null)
                {
                    subKeys.Add(child);
                }
            }
        }

        return new RegistryKeyBackup(hive.ToString(), keyPath, values, subKeys);
    }

    private bool RestoreTree(RegistryKeyBackup backup, string keyPath, out string? failure)
    {
        if (!_registry.CreateCurrentUserKey(keyPath, out failure))
        {
            return false;
        }

        foreach (var value in backup.Values)
        {
            if (!_registry.SetCurrentUserValue(keyPath, value, out failure))
            {
                return false;
            }
        }

        foreach (var child in backup.SubKeys)
        {
            // The stored path is where it came from; the leaf name is what places it under here.
            string name = child.KeyPath[(child.KeyPath.LastIndexOf('\\') + 1)..];
            if (!RestoreTree(child, $@"{keyPath}\{name}", out failure))
            {
                return false;
            }
        }

        failure = null;
        return true;
    }
}
