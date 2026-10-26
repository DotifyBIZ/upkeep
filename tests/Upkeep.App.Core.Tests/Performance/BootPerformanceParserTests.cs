using Upkeep.App.Core.Performance;

namespace Upkeep.App.Core.Tests.Performance;

public class BootPerformanceParserTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 7, 30, 0, TimeSpan.Zero);

    /// <summary>Event 100's leading properties as Windows delivers them: boot time is the sixth.</summary>
    private static object?[] BootProperties(object? bootTime) =>
        [(uint)2, At.UtcDateTime, At.UtcDateTime, (ulong)1, (ulong)1, bootTime, (uint)30_000, (uint)0];

    /// <summary>Event 101: file name third, friendly name fifth, total and degradation eighth and ninth.</summary>
    private static object?[] AppProperties(object? fileName, object? friendlyName, object? total, object? degradation) =>
        [At.UtcDateTime, At.UtcDateTime, fileName, (uint)5, friendlyName, (uint)7, "1.0.0.0", total, degradation];

    [Fact]
    public void ParseBoot_ReadsTheBootTimeByPosition()
    {
        var boot = BootPerformanceParser.ParseBoot(At, BootProperties((uint)46_250));

        Assert.Equal(new BootRecord(At, TimeSpan.FromMilliseconds(46_250)), boot);
    }

    [Theory]
    [InlineData(46_250L)]
    [InlineData(46_250)]
    [InlineData("46250")]
    public void ParseBoot_AcceptsTheIntegerShapesWindowsUses(object value)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(46_250), BootPerformanceParser.ParseBoot(At, BootProperties(value))?.Duration);
    }

    [Fact]
    public void ParseBoot_UnsignedLong_IsAccepted()
    {
        Assert.NotNull(BootPerformanceParser.ParseBoot(At, BootProperties((ulong)46_250)));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(3_600_001L)]
    public void ParseBoot_ZeroNegativeOrImplausible_IsDropped(long milliseconds)
    {
        Assert.Null(BootPerformanceParser.ParseBoot(At, BootProperties(milliseconds)));
    }

    [Fact]
    public void ParseBoot_TooFewProperties_IsDropped()
    {
        Assert.Null(BootPerformanceParser.ParseBoot(At, [(uint)2, At.UtcDateTime]));
    }

    [Fact]
    public void ParseBoot_UnreadableValue_IsDropped()
    {
        Assert.Null(BootPerformanceParser.ParseBoot(At, BootProperties("fast")));
        Assert.Null(BootPerformanceParser.ParseBoot(At, BootProperties(null)));
        Assert.Null(BootPerformanceParser.ParseBoot(At, BootProperties(ulong.MaxValue)));
    }

    [Fact]
    public void ParseSlowApp_ReadsNameAndTimesByPosition()
    {
        var app = BootPerformanceParser.ParseSlowApp(At, AppProperties("Steam.exe", "Steam Client Bootstrapper", (uint)2600, (uint)900));

        Assert.Equal(
            new SlowStartupApp(At, "Steam.exe", "Steam Client Bootstrapper", TimeSpan.FromMilliseconds(2600), TimeSpan.FromMilliseconds(900)),
            app);
    }

    [Fact]
    public void ParseSlowApp_PathInsteadOfName_KeepsOnlyTheFileName()
    {
        var app = BootPerformanceParser.ParseSlowApp(At, AppProperties(@" C:\Program Files\Steam\steam.exe ", "", (uint)2600, (uint)900));

        Assert.Equal("steam.exe", app?.FileName);
        Assert.Null(app?.FriendlyName);
    }

    [Fact]
    public void ParseSlowApp_MissingName_IsDropped()
    {
        Assert.Null(BootPerformanceParser.ParseSlowApp(At, AppProperties(" ", "x", (uint)1, (uint)1)));
        Assert.Null(BootPerformanceParser.ParseSlowApp(At, AppProperties(42, "x", (uint)1, (uint)1)));
    }

    [Fact]
    public void ParseSlowApp_UnreadableTimes_AreDropped()
    {
        Assert.Null(BootPerformanceParser.ParseSlowApp(At, AppProperties("a.exe", "a", "slow", (uint)1)));
        Assert.Null(BootPerformanceParser.ParseSlowApp(At, AppProperties("a.exe", "a", (uint)1, null)));
    }

    [Fact]
    public void ParseSlowApp_TooFewProperties_IsDropped()
    {
        Assert.Null(BootPerformanceParser.ParseSlowApp(At, [At.UtcDateTime, At.UtcDateTime, "a.exe"]));
    }
}
