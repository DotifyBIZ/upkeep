using System.Globalization;
using Upkeep.App.Core.Formatting;

namespace Upkeep.App.Core.Tests.Formatting;

public class ByteSizeTests
{
    public ByteSizeTests()
    {
        // Format() deliberately follows the current culture (Polish uses a comma as the decimal
        // separator), so every assertion here pins the culture rather than assuming the runner's.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(812L * 1024 * 1024, "812 MB")]
    public void Format_SmallerThanAGigabyte_UsesWholeUnits(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes));

    [Fact]
    public void Format_UnderTenGigabytes_KeepsOneDecimal()
    {
        long oneAndAHalfGigabytes = (long)(1.5 * 1024 * 1024 * 1024);

        Assert.Equal("1.5 GB", ByteSize.Format(oneAndAHalfGigabytes));
    }

    [Fact]
    public void Format_TenGigabytesOrMore_DropsTheDecimalAsNoise()
    {
        long twelveGigabytes = 12L * 1024 * 1024 * 1024;

        Assert.Equal("12 GB", ByteSize.Format(twelveGigabytes));
    }

    [Fact]
    public void Format_Terabytes_KeepsTwoDecimals()
    {
        long oneAndAQuarterTerabytes = (long)(1.25 * 1024 * 1024 * 1024 * 1024);

        Assert.Equal("1.25 TB", ByteSize.Format(oneAndAQuarterTerabytes));
    }

    [Fact]
    public void Format_NegativeInput_ReportsZeroRatherThanANegativeSize()
    {
        // A subtraction that went the wrong way should never render as "-3 GB" in the UI.
        Assert.Equal("0 B", ByteSize.Format(-1));
    }

    [Fact]
    public void Format_FollowsTheCurrentCulturesDecimalSeparator()
    {
        CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
        long oneAndAHalfGigabytes = (long)(1.5 * 1024 * 1024 * 1024);

        Assert.Equal("1,5 GB", ByteSize.Format(oneAndAHalfGigabytes));
    }
}
