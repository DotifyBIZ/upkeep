using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Upkeep.App.Core.Platform;

/// <summary>What the Recycle Bin currently holds.</summary>
public sealed record RecycleBinInfo(long SizeBytes, long ItemCount)
{
    public static readonly RecycleBinInfo Empty = new(0, 0);
}

/// <summary>Reads and empties the Recycle Bin through the shell's own API.</summary>
public interface IRecycleBin
{
    /// <summary>Size and item count across all drives, or <see cref="RecycleBinInfo.Empty"/> if
    /// Windows wouldn't say.</summary>
    RecycleBinInfo Query();

    /// <summary>Empties the bin for the signed-in user. Irreversible by definition — the UI says
    /// so before this is ever called (ADR-0006).</summary>
    bool Empty();
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin shell32 wrapper; there is no way to exercise it without destroying the runner's Recycle Bin.")]
public sealed partial class RecycleBin : IRecycleBin
{
    private const int Ok = 0;
    private const uint NoConfirmation = 0x00000001;
    private const uint NoProgressUi = 0x00000002;
    private const uint NoSound = 0x00000004;

    public RecycleBinInfo Query()
    {
        var info = new ShQueryRecycleBinInfo { CbSize = Marshal.SizeOf<ShQueryRecycleBinInfo>() };

        // A null root path means "every drive's bin", which is what the user sees in Explorer.
        return SHQueryRecycleBin(null, ref info) == Ok
            ? new RecycleBinInfo(info.Size, info.ItemCount)
            : RecycleBinInfo.Empty;
    }

    public bool Empty() =>
        // No confirmation dialog and no progress UI: Upkeep already got the user's confirmation in
        // its own preview, and a second shell prompt mid-run would be a surprise, not a safeguard.
        SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound) == Ok;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRecycleBinInfo
    {
        public int CbSize;
        public long Size;
        public long ItemCount;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBin(string? rootPath, ref ShQueryRecycleBinInfo info);

    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBin(IntPtr owner, string? rootPath, uint flags);
}
