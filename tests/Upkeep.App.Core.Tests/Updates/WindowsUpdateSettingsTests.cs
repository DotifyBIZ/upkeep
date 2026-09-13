using Upkeep.App.Core.Tests.Fakes;
using Upkeep.App.Core.Updates;

namespace Upkeep.App.Core.Tests.Updates;

public class WindowsUpdateSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeMachineRegistryWriter _registry = new();

    private WindowsUpdateSettings CreateSettings() => new(_registry, new FixedTimeProvider(Now));

    private void GivePauseExpiry(DateTimeOffset expiry) => _registry.SetString(
        WindowsUpdatePolicy.PauseKeyPath,
        WindowsUpdatePolicy.PauseExpiryValueName,
        WindowsUpdatePolicy.FormatTime(expiry),
        out _);

    [Fact]
    public void Read_NothingConfigured_IsNotPausedAndDefersNothing()
    {
        var state = CreateSettings().Read();

        Assert.False(state.IsPaused);
        Assert.Equal(0, state.DeferFeatureUpdatesDays);
        Assert.Equal(0, state.DeferQualityUpdatesDays);
    }

    [Fact]
    public void Read_PauseStillRunning_ReportsWhenItEnds()
    {
        GivePauseExpiry(Now.AddDays(5));

        var state = CreateSettings().Read();

        Assert.True(state.IsPaused);
        Assert.Equal(Now.AddDays(5), state.PausedUntil);
    }

    [Fact]
    public void Read_PauseThatHasExpired_IsNotPaused()
    {
        // Windows clears these lazily, so a stale value must not read as a live pause.
        GivePauseExpiry(Now.AddDays(-1));

        var state = CreateSettings().Read();

        Assert.False(state.IsPaused);
        Assert.Null(state.PausedUntil);
    }

    [Fact]
    public void Read_DeferralPolicies_AreReported()
    {
        _registry.SetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName, 180, out _);
        _registry.SetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.QualityDeferValueName, 14, out _);

        var state = CreateSettings().Read();

        Assert.Equal(180, state.DeferFeatureUpdatesDays);
        Assert.Equal(14, state.DeferQualityUpdatesDays);
    }

    [Fact]
    public void Pause_WritesBothTimesInTheShapeWindowsReads()
    {
        Assert.True(CreateSettings().Pause(7, out string? failure));
        Assert.Null(failure);

        Assert.Equal("2026-09-12T08:00:00Z", _registry.GetString(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseStartValueName));
        Assert.Equal("2026-09-19T08:00:00Z", _registry.GetString(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseExpiryValueName));
    }

    [Fact]
    public void Pause_LongerThanWindowsHonours_IsShortened()
    {
        CreateSettings().Pause(365, out _);

        Assert.Equal(
            WindowsUpdatePolicy.FormatTime(Now.AddDays(WindowsUpdatePolicy.MaximumPauseDays)),
            _registry.GetString(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseExpiryValueName));
    }

    [Fact]
    public void Pause_RegistryRefusedTheWrite_ReportsItRatherThanClaimingSuccess()
    {
        _registry.Unwritable.Add(WindowsUpdatePolicy.PauseKeyPath);

        Assert.False(CreateSettings().Pause(7, out string? failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void Resume_ClearsThePauseSoItReadsAsRunningAgain()
    {
        var settings = CreateSettings();
        settings.Pause(7, out _);

        Assert.True(settings.ResumeNow(out string? failure));
        Assert.Null(failure);
        Assert.False(settings.Read().IsPaused);
    }

    [Fact]
    public void SetDeferral_WritesBothPeriods()
    {
        Assert.True(CreateSettings().SetDeferral(180, 14, out _));

        Assert.Equal(180, _registry.GetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName));
        Assert.Equal(14, _registry.GetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.QualityDeferValueName));
    }

    [Fact]
    public void SetDeferral_BeyondWhatWindowsAccepts_IsClamped()
    {
        CreateSettings().SetDeferral(400, 90, out _);

        Assert.Equal(WindowsUpdatePolicy.MaximumFeatureDeferralDays, _registry.GetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName));
        Assert.Equal(WindowsUpdatePolicy.MaximumQualityDeferralDays, _registry.GetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.QualityDeferValueName));
    }

    [Fact]
    public void SetDeferral_RegistryRefusedTheWrite_ReportsIt()
    {
        _registry.Unwritable.Add(WindowsUpdatePolicy.DeferralKeyPath);

        Assert.False(CreateSettings().SetDeferral(30, 7, out string? failure));
        Assert.NotNull(failure);
    }
}
