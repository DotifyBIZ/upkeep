using Upkeep.App.Core.Updates;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>
/// A machine hive that only contains what a test put in it. The real one writes HKLM and only ever
/// runs inside the elevated helper, which is not somewhere a test should go.
/// </summary>
public sealed class FakeMachineRegistryWriter : IMachineRegistryWriter
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Key paths a test made unwritable, to exercise the failure paths.</summary>
    public HashSet<string> Unwritable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Single values a test made unwritable, as <c>keyPath!valueName</c>. A setting here is two
    /// registry values under one key, so failing the whole key cannot reach the case that matters:
    /// the first write landing and the second not.
    /// </summary>
    public HashSet<string> UnwritableValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> DeletedValues { get; } = [];

    public string? GetString(string keyPath, string valueName) =>
        _values.TryGetValue(Key(keyPath, valueName), out object? value) ? value as string : null;

    public int? GetInt32(string keyPath, string valueName) =>
        _values.TryGetValue(Key(keyPath, valueName), out object? value) ? value as int? : null;

    public bool SetString(string keyPath, string valueName, string value, out string? failure) =>
        Write(keyPath, valueName, value, out failure);

    public bool SetInt32(string keyPath, string valueName, int value, out string? failure) =>
        Write(keyPath, valueName, value, out failure);

    public bool DeleteValue(string keyPath, string valueName, out string? failure)
    {
        if (Unwritable.Contains(keyPath) || UnwritableValues.Contains(Key(keyPath, valueName)))
        {
            failure = "Access is denied.";
            return false;
        }

        _values.Remove(Key(keyPath, valueName));
        DeletedValues.Add(Key(keyPath, valueName));
        failure = null;
        return true;
    }

    private bool Write(string keyPath, string valueName, object value, out string? failure)
    {
        if (Unwritable.Contains(keyPath) || UnwritableValues.Contains(Key(keyPath, valueName)))
        {
            failure = "Access is denied.";
            return false;
        }

        _values[Key(keyPath, valueName)] = value;
        failure = null;
        return true;
    }

    private static string Key(string keyPath, string valueName) => $"{keyPath}!{valueName}";
}
