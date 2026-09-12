using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>
/// A registry that only contains what a test put in it. The decisions built on the real one — which
/// keys look like leftovers, which startup items are disabled — are the parts worth testing, and
/// none of them should need a real registry, still less write to one.
/// </summary>
public sealed class FakeRegistryProbe : IRegistryProbe
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys or values a test made unwritable, to exercise the failure paths.</summary>
    public HashSet<string> Unwritable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DeletedKeys { get; } = [];

    public List<string> DeletedValues { get; } = [];

    public void AddKey(RegistryHiveName hive, string keyPath) => _keys.Add(Key(hive, keyPath));

    public void AddValue(RegistryHiveName hive, string keyPath, string valueName, object value)
    {
        AddKey(hive, keyPath);
        _values[Value(hive, keyPath, valueName)] = value;
    }

    public bool KeyExists(RegistryHiveName hive, string keyPath) => _keys.Contains(Key(hive, keyPath));

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveName hive, string keyPath)
    {
        string prefix = Key(hive, keyPath) + "\\";

        return [.. _keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => key[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<string> GetValueNames(RegistryHiveName hive, string keyPath)
    {
        string prefix = Key(hive, keyPath) + "!";

        return [.. _values.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => key[prefix.Length..])];
    }

    public string? GetStringValue(RegistryHiveName hive, string keyPath, string valueName) =>
        _values.TryGetValue(Value(hive, keyPath, valueName), out object? value) ? value as string : null;

    public byte[]? GetBinaryValue(RegistryHiveName hive, string keyPath, string valueName) =>
        _values.TryGetValue(Value(hive, keyPath, valueName), out object? value) ? value as byte[] : null;

    public IReadOnlyList<RegistryValueSnapshot> GetValues(RegistryHiveName hive, string keyPath)
    {
        string prefix = Key(hive, keyPath) + "!";

        return [.. _values
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(pair => Snapshot(pair.Key[prefix.Length..], pair.Value))];
    }

    public bool CreateCurrentUserKey(string keyPath, out string? failure)
    {
        if (Unwritable.Contains(keyPath))
        {
            failure = "Access is denied.";
            return false;
        }

        AddKey(RegistryHiveName.CurrentUser, keyPath);
        failure = null;
        return true;
    }

    public bool SetCurrentUserValue(string keyPath, RegistryValueSnapshot value, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (Unwritable.Contains(keyPath))
        {
            failure = "Access is denied.";
            return false;
        }

        object? data = value.Kind switch
        {
            "Binary" => value.Binary,
            "MultiString" => value.Lines?.ToArray(),
            "DWord" or "QWord" => value.Number,
            _ => value.Text,
        };

        if (data is null)
        {
            failure = "The backup holds no usable data.";
            return false;
        }

        AddValue(RegistryHiveName.CurrentUser, keyPath, value.Name, data);
        failure = null;
        return true;
    }

    public bool SetCurrentUserBinaryValue(string keyPath, string valueName, byte[] value, out string? failure)
    {
        if (Unwritable.Contains(keyPath))
        {
            failure = "Access is denied.";
            return false;
        }

        AddValue(RegistryHiveName.CurrentUser, keyPath, valueName, value);
        failure = null;
        return true;
    }

    public bool DeleteCurrentUserValue(string keyPath, string valueName, out string? failure)
    {
        if (Unwritable.Contains(keyPath))
        {
            failure = "Access is denied.";
            return false;
        }

        _values.Remove(Value(RegistryHiveName.CurrentUser, keyPath, valueName));
        DeletedValues.Add($@"{keyPath}!{valueName}");
        failure = null;
        return true;
    }

    public bool DeleteCurrentUserKeyTree(string keyPath, out string? failure)
    {
        if (Unwritable.Contains(keyPath))
        {
            failure = "Access is denied.";
            return false;
        }

        DeletedKeys.Add(keyPath);
        _keys.Remove(Key(RegistryHiveName.CurrentUser, keyPath));
        failure = null;
        return true;
    }

    /// <summary>The kind a test's value stands for, inferred from what it actually is.</summary>
    private static RegistryValueSnapshot Snapshot(string name, object value) => value switch
    {
        byte[] binary => new RegistryValueSnapshot(name, "Binary", Binary: binary),
        string[] lines => new RegistryValueSnapshot(name, "MultiString", Lines: lines),
        int number => new RegistryValueSnapshot(name, "DWord", Number: number),
        long number => new RegistryValueSnapshot(name, "QWord", Number: number),
        _ => new RegistryValueSnapshot(name, "String", Text: value.ToString()),
    };

    private static string Key(RegistryHiveName hive, string keyPath) => $"{hive}:{keyPath}";

    private static string Value(RegistryHiveName hive, string keyPath, string valueName) =>
        $"{hive}:{keyPath}!{valueName}";
}
