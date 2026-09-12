using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Upkeep.App.Core.Platform;

/// <summary>
/// A file's identity on disk: the volume it lives on plus its index within that volume. Two paths
/// with the same identity are the same bytes — a hard link, not a copy.
/// </summary>
public readonly record struct FileId(uint VolumeSerialNumber, ulong FileIndex);

/// <summary>
/// Reads a file's on-disk identity.
/// <para>
/// The duplicate finder needs this to stay honest: hard-linked paths are byte-for-byte identical
/// and would look like perfect duplicates, but deleting one frees nothing at all. Offering that as
/// reclaimable space would be a lie told in gigabytes.
/// </para>
/// </summary>
public interface IFileIdentityReader
{
    /// <summary>The file's identity, or null when Windows won't say (the file vanished, or the
    /// filesystem doesn't report one).</summary>
    FileId? TryRead(string path);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin GetFileInformationByHandle wrapper; the duplicate grouping that uses it is tested against a substitute.")]
public sealed partial class FileIdentityReader : IFileIdentityReader
{
    public FileId? TryRead(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var information))
            {
                return null;
            }

            ulong index = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            return new FileId(information.VolumeSerialNumber, index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
}
