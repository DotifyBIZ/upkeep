using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Tests.Performance;

public class PowerModesTests
{
    [Theory]
    [InlineData(PowerMode.BestPowerEfficiency)]
    [InlineData(PowerMode.Balanced)]
    [InlineData(PowerMode.BestPerformance)]
    public void Ids_RoundTrip(PowerMode mode)
    {
        Assert.Equal(mode, PowerModes.FromId(PowerModes.ToId(mode)));
    }

    [Fact]
    public void FromId_ModeUpkeepDoesNotKnow_IsNull()
    {
        // Windows 10's "Better battery" slider position, which Windows 11 doesn't offer.
        Assert.Null(PowerModes.FromId(new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f")));
    }

    [Fact]
    public void SettingId_IsOnePerSource()
    {
        Assert.Equal(PerformanceSettingIds.PowerModePluggedIn, PowerModes.SettingId(PowerSource.PluggedIn));
        Assert.Equal(PerformanceSettingIds.PowerModeOnBattery, PowerModes.SettingId(PowerSource.OnBattery));
    }

    [Theory]
    [InlineData(PowerMode.BestPowerEfficiency, true, true)]
    [InlineData(PowerMode.BestPowerEfficiency, false, false)]
    [InlineData(PowerMode.Balanced, true, false)]
    public void IsThrottledWhilePluggedIn_OnlyWhenWindowsAppliesIt(PowerMode pluggedIn, bool applies, bool expected)
    {
        Assert.Equal(expected, new PowerModeState(pluggedIn, PowerMode.Balanced, applies, HasBattery: true).IsThrottledWhilePluggedIn);
    }
}
