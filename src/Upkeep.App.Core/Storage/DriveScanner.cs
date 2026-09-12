namespace Upkeep.App.Core.Storage;

/// <summary>Reads fixed-drive space. Ready drives only — a disconnected mapped drive or an empty
/// card reader throws on every property, so they're filtered rather than reported as 0 bytes.</summary>
public interface IDriveScanner
{
    IReadOnlyList<DriveSnapshot> GetFixedDrives();

    /// <summary>Free bytes on the drive containing <paramref name="path"/>, or null if unreadable.</summary>
    long? GetFreeBytes(string path);
}

/// <inheritdoc />
public sealed class DriveScanner : IDriveScanner
{
    public IReadOnlyList<DriveSnapshot> GetFixedDrives()
    {
        var drives = new List<DriveSnapshot>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                // VolumeLabel is often empty; the letter is what the user recognizes either way.
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel;
                drives.Add(new DriveSnapshot(label, drive.RootDirectory.FullName, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A drive that disappears mid-enumeration is not an error worth failing Home over.
            }
        }

        return drives;
    }

    public long? GetFreeBytes(string path)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(path) ?? path).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
