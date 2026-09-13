using Upkeep.App.Core.Updates;

namespace Upkeep.App.Core.Tests.Updates;

public class WindowsUpdatePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(7, 7)]
    [InlineData(1, 1)]
    [InlineData(35, 35)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(90, 35)]
    public void ClampPauseDays_StaysWithinWhatWindowsHonours(int asked, int expected)
    {
        // Windows offers up to 35 days; a control that says 90 would silently do 35.
        Assert.Equal(expected, WindowsUpdatePolicy.ClampPauseDays(asked));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(180, 180)]
    [InlineData(365, 365)]
    [InlineData(400, 365)]
    [InlineData(-1, 0)]
    public void ClampFeatureDeferralDays_StaysWithinAYear(int asked, int expected) =>
        Assert.Equal(expected, WindowsUpdatePolicy.ClampFeatureDeferralDays(asked));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    [InlineData(-1, 0)]
    public void ClampQualityDeferralDays_StaysWithinAMonth(int asked, int expected) =>
        Assert.Equal(expected, WindowsUpdatePolicy.ClampQualityDeferralDays(asked));

    [Fact]
    public void FormatTime_UsesTheShapeWindowsStores()
    {
        // Getting this wrong means a pause Windows reads as absent.
        Assert.Equal("2026-09-12T08:00:00Z", WindowsUpdatePolicy.FormatTime(Now));
    }

    [Fact]
    public void FormatTime_LocalTime_IsWrittenAsUtc()
    {
        var local = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-09-12T08:00:00Z", WindowsUpdatePolicy.FormatTime(local));
    }

    [Fact]
    public void ParseTime_ReadsBackWhatItWrote()
    {
        string written = WindowsUpdatePolicy.FormatTime(Now);

        Assert.Equal(Now, WindowsUpdatePolicy.ParseTime(written));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a time at all")]
    public void ParseTime_SomethingElseWroteNonsense_ReadsAsNotPaused(string? value)
    {
        // These values are written by Windows and by other tools, not only by Upkeep.
        Assert.Null(WindowsUpdatePolicy.ParseTime(value));
    }

    [Fact]
    public void PauseWindow_RunsFromNowForTheDaysAsked()
    {
        (string start, string expiry) = WindowsUpdatePolicy.PauseWindow(Now, 7);

        Assert.Equal("2026-09-12T08:00:00Z", start);
        Assert.Equal("2026-09-19T08:00:00Z", expiry);
    }

    [Fact]
    public void PauseWindow_MoreDaysThanWindowsAllows_IsShortenedNotRefused()
    {
        (_, string expiry) = WindowsUpdatePolicy.PauseWindow(Now, 365);

        Assert.Equal(WindowsUpdatePolicy.FormatTime(Now.AddDays(WindowsUpdatePolicy.MaximumPauseDays)), expiry);
    }

    [Fact]
    public void IsPauseActive_ExpiryInTheFuture_IsPaused() =>
        Assert.True(WindowsUpdatePolicy.IsPauseActive(Now.AddDays(3), Now));

    [Fact]
    public void IsPauseActive_ExpiryHasPassed_IsNotPaused()
    {
        // Windows clears these lazily, so a stale value must not read as a live pause.
        Assert.False(WindowsUpdatePolicy.IsPauseActive(Now.AddDays(-1), Now));
    }

    [Fact]
    public void IsPauseActive_NothingRecorded_IsNotPaused() =>
        Assert.False(WindowsUpdatePolicy.IsPauseActive(null, Now));

    [Fact]
    public void State_NotConfigured_IsNotPaused()
    {
        Assert.False(WindowsUpdateState.NotConfigured.IsPaused);
        Assert.Equal(0, WindowsUpdateState.NotConfigured.DeferFeatureUpdatesDays);
    }

    [Fact]
    public void PauseDaysRemaining_PauseStillRunning_IsTheDaysItHasLeft()
    {
        // Ten days on the clock comes back as ten days, not as the length of the original pause.
        Assert.Equal(10, WindowsUpdatePolicy.PauseDaysRemaining(Now.AddDays(10), Now));
    }

    [Fact]
    public void PauseDaysRemaining_PartOfADayLeft_RoundsUpRatherThanToNothing()
    {
        // The request takes whole days, and rounding 6 hours down would silently resume updates.
        Assert.Equal(1, WindowsUpdatePolicy.PauseDaysRemaining(Now.AddHours(6), Now));
    }

    [Fact]
    public void PauseDaysRemaining_PauseHasLapsed_IsResumeRatherThanAFreshPause()
    {
        // Putting a session back must not start a pause the user never asked for.
        Assert.Equal(0, WindowsUpdatePolicy.PauseDaysRemaining(Now.AddDays(-1), Now));
    }

    [Fact]
    public void PauseDaysRemaining_NothingRecorded_IsResume() =>
        Assert.Equal(0, WindowsUpdatePolicy.PauseDaysRemaining(null, Now));

    [Fact]
    public void PauseDaysRemaining_LongerThanWindowsAllows_IsShortened() =>
        Assert.Equal(WindowsUpdatePolicy.MaximumPauseDays, WindowsUpdatePolicy.PauseDaysRemaining(Now.AddDays(400), Now));

    [Fact]
    public void Deferral_RoundTripsThroughAJournalEntry()
    {
        string recorded = WindowsUpdatePolicy.FormatDeferral(180, 7);

        Assert.True(WindowsUpdatePolicy.TryParseDeferral(recorded, out int featureDays, out int qualityDays));
        Assert.Equal(180, featureDays);
        Assert.Equal(7, qualityDays);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("180")]
    [InlineData("180/7/1")]
    [InlineData("a lot/7")]
    [InlineData("180/soon")]
    public void TryParseDeferral_AnythingElse_IsRefusedRatherThanGuessedAt(string? recorded)
    {
        // A journal from another build, or a hand-edited one, must not revert to invented numbers.
        Assert.False(WindowsUpdatePolicy.TryParseDeferral(recorded, out int featureDays, out int qualityDays));
        Assert.Equal(0, featureDays);
        Assert.Equal(0, qualityDays);
    }

}
