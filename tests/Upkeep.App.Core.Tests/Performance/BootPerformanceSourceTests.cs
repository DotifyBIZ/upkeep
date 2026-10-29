using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Performance;

public class BootPerformanceSourceTests
{
    private readonly FakeElevationService _elevation = new();

    private static BootPerformance OneBoot() => new([new BootRecord(DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(40))], []);

    [Fact]
    public async Task ReadAsync_ReadsOnceAndKeepsIt()
    {
        var performance = OneBoot();
        _elevation.EnqueueResponse(new BootPerformanceResponse(performance));
        var source = new BootPerformanceSource(_elevation);

        Assert.Equal(BootPerformanceStatus.Read, await source.ReadAsync(allowPrompt: true));
        Assert.Equal(BootPerformanceStatus.Read, await source.ReadAsync(allowPrompt: true));

        Assert.Same(performance, source.Cached);
        Assert.IsType<ReadBootPerformanceRequest>(Assert.Single(_elevation.SentRequests));
    }

    [Fact]
    public async Task ReadAsync_WithoutPromptAndNotElevated_AsksNothing()
    {
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);
        var source = new BootPerformanceSource(_elevation);

        Assert.Equal(BootPerformanceStatus.NeedsApproval, await source.ReadAsync(allowPrompt: false));
        Assert.Empty(_elevation.SentRequests);
    }

    [Fact]
    public async Task ReadAsync_WithoutPromptButAlreadyElevated_Reads()
    {
        _elevation.EnqueueResponse(new BootPerformanceResponse(OneBoot()));

        Assert.Equal(BootPerformanceStatus.Read, await new BootPerformanceSource(_elevation).ReadAsync(allowPrompt: false));
    }

    [Theory]
    [InlineData(ElevationStatus.Declined, BootPerformanceStatus.Declined)]
    [InlineData(ElevationStatus.Unavailable, BootPerformanceStatus.Failed)]
    public async Task ReadAsync_NoHelper_SaysWhy(ElevationStatus elevation, BootPerformanceStatus expected)
    {
        _elevation.Availability = new ElevationResult(elevation);

        Assert.Equal(expected, await new BootPerformanceSource(_elevation).ReadAsync(allowPrompt: true));
    }

    [Fact]
    public async Task ReadAsync_HelperAnswersWithAnError_IsAFailureAndNothingIsCached()
    {
        _elevation.EnqueueResponse(new HelperErrorResponse(HelperErrorCodes.WindowsRefused, "Access is denied."));
        var source = new BootPerformanceSource(_elevation);

        Assert.Equal(BootPerformanceStatus.Failed, await source.ReadAsync(allowPrompt: true));
        Assert.Null(source.Cached);
    }
}
