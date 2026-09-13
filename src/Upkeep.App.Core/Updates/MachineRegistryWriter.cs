using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace Upkeep.App.Core.Updates;

/// <summary>Writes machine-wide registry values. Only ever constructed inside the elevated helper.</summary>
public interface IMachineRegistryWriter
{
    string? GetString(string keyPath, string valueName);

    int? GetInt32(string keyPath, string valueName);

    bool SetString(string keyPath, string valueName, string value, out string? failure);

    bool SetInt32(string keyPath, string valueName, int value, out string? failure);

    bool DeleteValue(string keyPath, string valueName, out string? failure);
}

/// <summary>
/// HKLM writes, deliberately kept out of <see cref="Platform.IRegistryProbe"/>.
/// <para>
/// The shell's registry seam is current-user-only by design (ADR-0005), and widening it so the
/// Windows Update page could write policy keys would hand every caller in the shell the ability to
/// write HKLM. This type exists only in the helper process, where administrator rights are the
/// point, and it is reachable solely through the closed set of helper operations.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin HKLM wrapper that only runs inside the elevated helper; what gets written is decided in WindowsUpdatePolicy, which is tested.")]
public sealed class MachineRegistryWriter : IMachineRegistryWriter
{
    public string? GetString(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public int? GetInt32(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as int?;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public bool SetString(string keyPath, string valueName, string value, out string? failure) =>
        Write(keyPath, valueName, value, RegistryValueKind.String, out failure);

    public bool SetInt32(string keyPath, string valueName, int value, out string? failure) =>
        Write(keyPath, valueName, value, RegistryValueKind.DWord, out failure);

    public bool DeleteValue(string keyPath, string valueName, out string? failure)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
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

    private static bool Write(string keyPath, string valueName, object value, RegistryValueKind kind, out string? failure)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(keyPath, writable: true);
            key.SetValue(valueName, value, kind);
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            failure = ex.Message;
            return false;
        }
    }
}
