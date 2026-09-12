using Upkeep.App.Core.Startup;

namespace Upkeep.App.Core.Tests.Startup;

public class StartupApprovedValueTests
{
    private static readonly DateTimeOffset DisabledAt = new(2026, 9, 12, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void IsEnabled_NoValueYet_MeansEnabled()
    {
        // Windows only writes this value once something has changed the item's state, so its
        // absence is the normal case for an item that runs at sign-in.
        Assert.True(StartupApprovedValue.IsEnabled(null));
        Assert.True(StartupApprovedValue.IsEnabled([]));
    }

    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x06, true)]
    [InlineData(0x03, false)]
    [InlineData(0x07, false)]
    public void IsEnabled_ReadsTheFlagWindowsActuallyWrites(byte flag, bool expected)
    {
        // Windows has used 0x02/0x06 for enabled and 0x03/0x07 for disabled over the years; the
        // low bit is what carries the state in all of them.
        byte[] value = new byte[StartupApprovedValue.ValueLength];
        value[0] = flag;

        Assert.Equal(expected, StartupApprovedValue.IsEnabled(value));
    }

    [Fact]
    public void Enabled_ProducesTheTwelveBytesTaskManagerWrites()
    {
        byte[] value = StartupApprovedValue.Enabled();

        Assert.Equal(StartupApprovedValue.ValueLength, value.Length);
        Assert.True(StartupApprovedValue.IsEnabled(value));
        Assert.Null(StartupApprovedValue.DisabledAt(value));
    }

    [Fact]
    public void Disabled_RoundTripsTheTimeItWasTurnedOff()
    {
        byte[] value = StartupApprovedValue.Disabled(DisabledAt);

        Assert.Equal(StartupApprovedValue.ValueLength, value.Length);
        Assert.False(StartupApprovedValue.IsEnabled(value));
        Assert.Equal(DisabledAt, StartupApprovedValue.DisabledAt(value));
    }

    [Fact]
    public void DisabledAt_EnabledItem_HasNoTimestamp() =>
        Assert.Null(StartupApprovedValue.DisabledAt(StartupApprovedValue.Enabled()));

    [Fact]
    public void DisabledAt_TruncatedValue_IsIgnoredRatherThanMisread()
    {
        // This comes out of the registry, where anything can write anything.
        Assert.Null(StartupApprovedValue.DisabledAt([0x03, 0x00]));
    }

    [Fact]
    public void DisabledAt_NonsenseTimestamp_IsReportedAsUnknown()
    {
        byte[] value = new byte[StartupApprovedValue.ValueLength];
        value[0] = 0x03;
        // A file time that isn't a real date: reported as unknown, never as the year 1601.
        Array.Fill(value, (byte)0xFF, 4, 8);

        Assert.Null(StartupApprovedValue.DisabledAt(value));
    }

    [Fact]
    public void DisabledAt_ZeroTimestamp_IsUnknownNotTheEpoch()
    {
        byte[] value = new byte[StartupApprovedValue.ValueLength];
        value[0] = 0x03;

        Assert.Null(StartupApprovedValue.DisabledAt(value));
    }

    [Fact]
    public void EnabledAndDisabled_AreDistinguishableFromEachOther()
    {
        Assert.True(StartupApprovedValue.IsEnabled(StartupApprovedValue.Enabled()));
        Assert.False(StartupApprovedValue.IsEnabled(StartupApprovedValue.Disabled(DisabledAt)));
    }
}
