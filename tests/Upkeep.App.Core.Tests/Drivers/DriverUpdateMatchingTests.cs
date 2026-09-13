using Upkeep.App.Core.Drivers;

namespace Upkeep.App.Core.Tests.Drivers;

public class DriverUpdateMatchingTests
{
    private static DriverInfo Driver(string deviceName = "Realtek High Definition Audio") =>
        new(deviceName, "Realtek", "6.0.9285.1", null, @"PCI\VEN_10EC", "Media");

    [Fact]
    public void HasUpdate_UpdateNamesTheSameModel_IsAMatch()
    {
        var updates = new[] { new DriverUpdate("Realtek - MEDIA - 6.0.9508.1", "Realtek High Definition Audio") };

        Assert.True(DriverCatalog.HasUpdate(Driver(), updates));
    }

    [Fact]
    public void HasUpdate_ModelDiffersOnlyInCase_IsStillAMatch()
    {
        var updates = new[] { new DriverUpdate("Some title", "REALTEK HIGH DEFINITION AUDIO") };

        Assert.True(DriverCatalog.HasUpdate(Driver(), updates));
    }

    [Fact]
    public void HasUpdate_UpdateDeclaresNoModel_FallsBackToTheTitle()
    {
        // Plenty of driver updates carry no DriverModel, and the device name is in the title.
        var updates = new[] { new DriverUpdate("Realtek High Definition Audio - 6.0.9508.1", null) };

        Assert.True(DriverCatalog.HasUpdate(Driver(), updates));
    }

    [Fact]
    public void HasUpdate_ForADifferentDevice_IsNotAMatch()
    {
        // Claiming an update exists for the wrong device is worse than not spotting one.
        var updates = new[] { new DriverUpdate("NVIDIA - Display - 552.44", "NVIDIA GeForce RTX 4070") };

        Assert.False(DriverCatalog.HasUpdate(Driver(), updates));
    }

    [Fact]
    public void HasUpdate_NothingOnOffer_IsNotAMatch() =>
        Assert.False(DriverCatalog.HasUpdate(Driver(), []));

    [Fact]
    public void HasUpdate_DriverWithoutAName_NeverMatches()
    {
        // An empty name would otherwise be "contained" in every title there is.
        var updates = new[] { new DriverUpdate("Anything at all", null) };

        Assert.False(DriverCatalog.HasUpdate(Driver(deviceName: "   "), updates));
    }
}
