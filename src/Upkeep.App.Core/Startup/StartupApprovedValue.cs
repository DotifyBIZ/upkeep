using System.Buffers.Binary;

namespace Upkeep.App.Core.Startup;

/// <summary>
/// The 12-byte value Task Manager writes under
/// <c>…\Explorer\StartupApproved\{Run|Run32|StartupFolder}</c> to enable or disable a startup item.
/// <para>
/// Upkeep uses exactly this mechanism rather than deleting the Run entry (CLAUDE.md: prefer
/// Windows' own tooling). Deleting would work once and lose the item forever; writing this value is
/// what Task Manager itself does, is reversible, and keeps the two tools agreeing about state.
/// </para>
/// <para>
/// Layout: byte 0 carries the enabled/disabled flag, bytes 4-11 hold a FILETIME of when it was
/// disabled (zero while enabled). The remaining bytes are zero.
/// </para>
/// </summary>
public static class StartupApprovedValue
{
    /// <summary>Windows writes 12 bytes; anything else is not a value this code wrote.</summary>
    public const int ValueLength = 12;

    private const byte EnabledFlag = 0x02;
    private const byte DisabledFlag = 0x03;

    /// <summary>
    /// Whether the item runs at sign-in. A missing value means enabled — Windows only writes this
    /// value once something has changed the item's state.
    /// </summary>
    public static bool IsEnabled(byte[]? value)
    {
        if (value is null || value.Length == 0)
        {
            return true;
        }

        // Windows has used 0x02/0x06 for enabled and 0x03/0x07 for disabled; the low bit is what
        // actually carries the state.
        return (value[0] & 0x01) == 0;
    }

    /// <summary>Builds the value that enables an item.</summary>
    public static byte[] Enabled()
    {
        byte[] value = new byte[ValueLength];
        value[0] = EnabledFlag;
        return value;
    }

    /// <summary>
    /// Builds the value that disables an item, stamped with when it happened — the same timestamp
    /// Task Manager shows in its "Disabled" column.
    /// </summary>
    public static byte[] Disabled(DateTimeOffset disabledAt)
    {
        byte[] value = new byte[ValueLength];
        value[0] = DisabledFlag;
        BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(4), disabledAt.UtcDateTime.ToFileTimeUtc());
        return value;
    }

    /// <summary>
    /// When the item was disabled, or null if it is enabled or Windows recorded no time. Values
    /// that don't parse as a date are reported as null rather than as a nonsense year: this is
    /// read from the registry, where anything can be written by anything.
    /// </summary>
    public static DateTimeOffset? DisabledAt(byte[]? value)
    {
        if (value is null || value.Length < ValueLength || IsEnabled(value))
        {
            return null;
        }

        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(value.AsSpan(4));
        if (fileTime <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(fileTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
