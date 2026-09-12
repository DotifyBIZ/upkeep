using Upkeep.App.Core.Storage;

namespace Upkeep.App.Core.Tests.Storage;

public class DriveSnapshotTests
{
    [Fact]
    public void UsedFraction_HalfFullDrive_IsAHalf()
    {
        var drive = new DriveSnapshot("Windows (C:)", @"C:\", TotalBytes: 1000, FreeBytes: 500);

        Assert.Equal(0.5, drive.UsedFraction);
        Assert.Equal(500, drive.UsedBytes);
    }

    [Fact]
    public void UsedFraction_ZeroSizedDrive_IsZeroRatherThanDividingByZero()
    {
        var drive = new DriveSnapshot("Empty", @"E:\", TotalBytes: 0, FreeBytes: 0);

        Assert.Equal(0, drive.UsedFraction);
    }

    [Fact]
    public void UsedFraction_MoreFreeThanTotal_ClampsInsteadOfGoingNegative()
    {
        // Windows can report free space above the total on a drive with quotas or compression;
        // a progress bar bound to a negative fraction renders as a glitch.
        var drive = new DriveSnapshot("Quota", @"Q:\", TotalBytes: 100, FreeBytes: 150);

        Assert.Equal(0, drive.UsedFraction);
        Assert.Equal(0, drive.UsedBytes);
    }
}
