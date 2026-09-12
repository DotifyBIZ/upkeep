using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>
/// A registry that only contains what a test put in it. The leftover scanner's decisions are the
/// part worth testing, and they shouldn't need a real registry — still less one that gets written to.
/// </summary>
public sealed class FakeRegistryProbe : IRegistryProbe
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public void AddKey(RegistryHiveName hive, string keyPath) => _keys.Add($"{hive}:{keyPath}");

    public bool KeyExists(RegistryHiveName hive, string keyPath) => _keys.Contains($"{hive}:{keyPath}");

    /// <summary>Keys a test made undeletable, to exercise the failure path.</summary>
    public HashSet<string> Undeletable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DeletedKeys { get; } = [];

    public bool DeleteCurrentUserKeyTree(string keyPath, out string? failure)
    {
        if (Undeletable.Contains(keyPath))
        {
            failure = "Access is denied.";
            return false;
        }

        DeletedKeys.Add(keyPath);
        _keys.Remove($"{RegistryHiveName.CurrentUser}:{keyPath}");
        failure = null;
        return true;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveName hive, string keyPath)
    {
        string prefix = $"{hive}:{keyPath}\\";

        return [.. _keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => key[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
