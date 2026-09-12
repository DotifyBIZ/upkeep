using Upkeep.App.Core.Safety;

namespace Upkeep.App.Core.Tests.Safety;

public class RestorePointPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CoversThisRun_PointFromAnHourAgo_IsEnough() =>
        Assert.True(RestorePointPolicy.CoversThisRun(Now.AddHours(-1), Now));

    [Fact]
    public void CoversThisRun_PointFromTwoDaysAgo_IsNot() =>
        Assert.False(RestorePointPolicy.CoversThisRun(Now.AddDays(-2), Now));

    [Fact]
    public void CoversThisRun_NoPointsAtAll_IsNot() =>
        Assert.False(RestorePointPolicy.CoversThisRun(null, Now));

    [Fact]
    public void CoversThisRun_ExactlyAtTheThrottleBoundary_IsNotCovered()
    {
        // Windows' throttle is 24 hours; at the boundary it will make a new one, so claiming the
        // old one covers the run would be the wrong way to be wrong.
        Assert.False(RestorePointPolicy.CoversThisRun(Now - RestorePointPolicy.CreationThrottle, Now));
    }

    [Fact]
    public void Evaluate_NewPointMade_IsCreated() =>
        Assert.Equal(RestorePointStatus.Created, RestorePointPolicy.Evaluate(true, Now, Now));

    [Fact]
    public void Evaluate_NoNewPointButARecentOneExists_IsReused()
    {
        // Windows returns success even when its throttle means nothing was written; the honest
        // record is the point that already covers this run, not a new one that doesn't exist.
        Assert.Equal(RestorePointStatus.ReusedRecent, RestorePointPolicy.Evaluate(false, Now.AddHours(-3), Now));
    }

    [Fact]
    public void Evaluate_NoPointAndNothingRecent_IsUnavailable() =>
        Assert.Equal(RestorePointStatus.Unavailable, RestorePointPolicy.Evaluate(false, null, Now));

    [Fact]
    public void Evaluate_NoPointAndOnlyAnOldOne_IsUnavailable() =>
        Assert.Equal(RestorePointStatus.Unavailable, RestorePointPolicy.Evaluate(false, Now.AddDays(-9), Now));

    [Fact]
    public void CreationThrottle_MatchesWindowsOwnTwentyFourHourWindow() =>
        Assert.Equal(TimeSpan.FromHours(24), RestorePointPolicy.CreationThrottle);

    [Theory]
    [InlineData(RestorePointStatus.Created, true)]
    [InlineData(RestorePointStatus.ReusedRecent, true)]
    [InlineData(RestorePointStatus.Unavailable, false)]
    [InlineData(RestorePointStatus.NotElevated, false)]
    public void IsCovered_SaysWhetherARunHasSomethingToRollBackTo(RestorePointStatus status, bool expected) =>
        Assert.Equal(expected, new RestorePointResult(status).IsCovered);
}
