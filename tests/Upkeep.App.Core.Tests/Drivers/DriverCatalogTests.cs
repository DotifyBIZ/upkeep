using Upkeep.App.Core.Drivers;

namespace Upkeep.App.Core.Tests.Drivers;

public class DriverCatalogTests
{
    private static DriverInfo Driver(
        string deviceName = "Realtek High Definition Audio",
        string version = "6.0.9285.1",
        string? manufacturer = "Realtek",
        string? deviceId = @"PCI\VEN_10EC") =>
        new(deviceName, manufacturer, version, new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), deviceId, "Media");

    [Fact]
    public void Worth_OneDriverSharedBySeveralDevices_IsListedOnce()
    {
        // Win32_PnPSignedDriver returns a row per device, so one package on three ports is three
        // rows — and a list that repeats itself reads as noise.
        var drivers = DriverCatalog.Worth([Driver(), Driver(), Driver()]);

        Assert.Single(drivers);
    }

    [Fact]
    public void Worth_SameDeviceWithADifferentVersion_IsKept()
    {
        var drivers = DriverCatalog.Worth([Driver(version: "6.0.9285.1"), Driver(version: "6.0.9508.1")]);

        Assert.Equal(2, drivers.Count);
    }

    [Fact]
    public void Worth_NamesDifferingOnlyInCase_AreTheSameDevice()
    {
        var drivers = DriverCatalog.Worth([Driver(deviceName: "Realtek Audio"), Driver(deviceName: "REALTEK AUDIO")]);

        Assert.Single(drivers);
    }

    [Fact]
    public void Worth_EntryWithoutAName_IsNotShown()
    {
        // Nobody would recognize it, and it can't be opened in Device Manager by name.
        var drivers = DriverCatalog.Worth([Driver(deviceName: ""), Driver(deviceName: "   "), Driver()]);

        Assert.Single(drivers);
    }

    [Fact]
    public void Worth_EntryWithoutAVersion_IsNotShown()
    {
        // There is nothing to compare against an available update.
        var drivers = DriverCatalog.Worth([Driver(version: ""), Driver()]);

        Assert.Single(drivers);
    }

    [Fact]
    public void Worth_IsOrderedByDeviceName()
    {
        var drivers = DriverCatalog.Worth([
            Driver(deviceName: "NVIDIA GeForce RTX 4070"),
            Driver(deviceName: "Intel UHD Graphics"),
            Driver(deviceName: "Realtek High Definition Audio"),
        ]);

        Assert.Equal(
            ["Intel UHD Graphics", "NVIDIA GeForce RTX 4070", "Realtek High Definition Audio"],
            drivers.Select(driver => driver.DeviceName));
    }

    [Fact]
    public void Worth_KeepsWhatARollbackNeeds()
    {
        var driver = Assert.Single(DriverCatalog.Worth([Driver()]));

        Assert.Equal(@"PCI\VEN_10EC", driver.DeviceId);
        Assert.Equal("Realtek", driver.Manufacturer);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), driver.Date);
    }

    [Fact]
    public void Worth_NothingInstalled_IsEmptyRatherThanAnError()
    {
        Assert.Empty(DriverCatalog.Worth([]));
    }
}
