namespace Upkeep.App.Core.Storage;

/// <summary>One fixed drive's space, as shown on Home and used to report what a cleanup freed.</summary>
public sealed record DriveSnapshot(string Name, string RootPath, long TotalBytes, long FreeBytes)
{
    /// <summary>Share of the drive in use, 0-1. Zero-sized drives report 0 rather than dividing by zero.</summary>
    public double UsedFraction => TotalBytes <= 0 ? 0 : Math.Clamp((TotalBytes - FreeBytes) / (double)TotalBytes, 0, 1);

    public long UsedBytes => Math.Max(TotalBytes - FreeBytes, 0);
}
