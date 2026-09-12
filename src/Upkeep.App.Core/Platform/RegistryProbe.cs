using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace Upkeep.App.Core.Platform;

/// <summary>Which registry hive a path belongs to. Only the two Upkeep ever touches.</summary>
public enum RegistryHiveName
{
    /// <summary>HKEY_CURRENT_USER — the signed-in user's own settings.</summary>
    CurrentUser,

    /// <summary>HKEY_LOCAL_MACHINE — machine-wide, and only writable through the elevated helper.</summary>
    LocalMachine,
}

/// <summary>
/// Reads the registry, and writes the small set of per-user values Upkeep is allowed to change.
/// <para>
/// A seam rather than direct calls, because the decisions built on it — which keys look like an
/// uninstalled app's leftovers, which startup items are disabled — are the parts worth testing,
/// and none of them should need a real registry, still less write to one.
/// </para>
/// <para>
/// Every write here is current-user only, by design: machine-wide changes belong to the elevated
/// helper (ADR-0005), so the shell has no method that could make one by accident.
/// </para>
/// </summary>
public interface IRegistryProbe
{
    bool KeyExists(RegistryHiveName hive, string keyPath);

    /// <summary>Immediate subkey names, or empty when the key is missing or unreadable.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHiveName hive, string keyPath);

    /// <summary>Value names directly under a key, or empty when it is missing or unreadable.</summary>
    IReadOnlyList<string> GetValueNames(RegistryHiveName hive, string keyPath);

    /// <summary>A string value, or null when it is missing or of another type.</summary>
    string? GetStringValue(RegistryHiveName hive, string keyPath, string valueName);

    /// <summary>A binary value, or null when it is missing or of another type.</summary>
    byte[]? GetBinaryValue(RegistryHiveName hive, string keyPath, string valueName);

    /// <summary>Writes a binary value in the current user's hive, creating the key if needed.</summary>
    bool SetCurrentUserBinaryValue(string keyPath, string valueName, byte[] value, out string? failure);

    /// <summary>Removes a value from the current user's hive.</summary>
    bool DeleteCurrentUserValue(string keyPath, string valueName, out string? failure);

    /// <summary>
    /// Deletes a key and everything under it, in the current user's hive only. Machine-wide keys
    /// are the elevated helper's business, and a test of the code that decides *which* key to
    /// remove should never be able to remove a real one.
    /// </summary>
    bool DeleteCurrentUserKeyTree(string keyPath, out string? failure);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin registry wrapper; every decision made with it is tested against a substitute.")]
public sealed class RegistryProbe : IRegistryProbe
{
    public bool KeyExists(RegistryHiveName hive, string keyPath)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveName hive, string keyPath)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> GetValueNames(RegistryHiveName hive, string keyPath)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetValueNames() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public string? GetStringValue(RegistryHiveName hive, string keyPath, string valueName)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public byte[]? GetBinaryValue(RegistryHiveName hive, string keyPath, string valueName)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetValue(valueName) as byte[];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public bool SetCurrentUserBinaryValue(string keyPath, string valueName, byte[] value, out string? failure)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            key.SetValue(valueName, value, RegistryValueKind.Binary);
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            failure = ex.Message;
            return false;
        }
    }

    public bool DeleteCurrentUserValue(string keyPath, string valueName, out string? failure)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            failure = ex.Message;
            return false;
        }
    }

    public bool DeleteCurrentUserKeyTree(string keyPath, out string? failure)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            failure = ex.Message;
            return false;
        }
    }

    private static RegistryKey Root(RegistryHiveName hive) =>
        hive == RegistryHiveName.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
}
