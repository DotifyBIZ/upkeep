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
/// Reads the registry. A seam rather than direct calls, because what the leftover scanner decides
/// — which keys look like they belong to the app that was just uninstalled — is the part worth
/// testing, and it should not need a real registry to test it.
/// </summary>
public interface IRegistryProbe
{
    bool KeyExists(RegistryHiveName hive, string keyPath);

    /// <summary>Immediate subkey names, or empty when the key is missing or unreadable.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHiveName hive, string keyPath);

    /// <summary>
    /// Deletes a key and everything under it, in the current user's hive only. Machine-wide keys
    /// are the elevated helper's business, and a test of the code that decides *which* key to
    /// remove should never be able to remove a real one.
    /// </summary>
    bool DeleteCurrentUserKeyTree(string keyPath, out string? failure);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin registry wrapper; the leftover-matching decisions that use it are tested against a substitute.")]
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
