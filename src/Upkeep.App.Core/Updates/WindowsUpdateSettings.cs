namespace Upkeep.App.Core.Updates;

/// <summary>What a pause or deferral change achieved, and how Windows Update now stands.</summary>
public sealed record WindowsUpdateChangeResult(bool Success, WindowsUpdateState State, string? FailureDetail);

/// <summary>Reads and changes how Windows Update is scheduled. Helper-side: every write is HKLM.</summary>
public interface IWindowsUpdateSettings
{
    WindowsUpdateState Read();

    /// <summary>Pauses updates for the days asked, clamped to what Windows honours.</summary>
    bool Pause(int days, out string? failure);

    /// <summary>Ends a pause now, rather than waiting for it to expire.</summary>
    bool ResumeNow(out string? failure);

    /// <summary>Sets how long feature and quality updates are held back.</summary>
    bool SetDeferral(int featureDays, int qualityDays, out string? failure);
}

/// <summary>
/// The Windows Update schedule, as Upkeep is willing to change it.
/// <para>
/// Values and formats come from <see cref="WindowsUpdatePolicy"/>, which is pure and tested; this
/// class only moves them in and out of the machine hive. Deferral is written whatever the edition
/// says — Home ignores those keys, so the *page* hides the controls rather than this refusing them
/// (ADR-0009).
/// </para>
/// <para>
/// Each setting here is two registry values, and the registry gives us no way to write a pair at
/// once. So every method below is ordered such that a failure part-way leaves the safer of the two
/// states, and then undoes the write that did land. A half-applied schedule would otherwise
/// outlive the failure it came from, with the page describing a machine that does not exist.
/// </para>
/// </summary>
public sealed class WindowsUpdateSettings : IWindowsUpdateSettings
{
    private readonly IMachineRegistryWriter _registry;
    private readonly TimeProvider _timeProvider;

    public WindowsUpdateSettings(IMachineRegistryWriter registry, TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public WindowsUpdateState Read()
    {
        var expiry = WindowsUpdatePolicy.ParseTime(
            _registry.GetString(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseExpiryValueName));

        // A pause whose time has passed is over, whatever the value still says.
        bool paused = WindowsUpdatePolicy.IsPauseActive(expiry, _timeProvider.GetUtcNow());

        return new WindowsUpdateState(
            paused ? expiry : null,
            _registry.GetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName) ?? 0,
            _registry.GetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.QualityDeferValueName) ?? 0);
    }

    public bool Pause(int days, out string? failure)
    {
        (string start, string expiry) = WindowsUpdatePolicy.PauseWindow(_timeProvider.GetUtcNow(), days);

        if (!_registry.SetString(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseStartValueName, start, out failure))
        {
            return false;
        }

        if (_registry.SetString(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseExpiryValueName, expiry, out failure))
        {
            return true;
        }

        // Half a pause window is not a pause: Windows and Read both key off the expiry, so nothing
        // is paused. Take the start value back out rather than leave a stray one behind. Best
        // effort by design — the caller keeps the real failure, not this cleanup's.
        _registry.DeleteValue(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseStartValueName, out _);
        return false;
    }

    public bool ResumeNow(out string? failure)
    {
        // Expiry first, so a failure on the second delete still leaves the pause genuinely over.
        // A start value on its own means nothing to Windows, and Read ignores it too.
        if (!_registry.DeleteValue(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseExpiryValueName, out failure))
        {
            return false;
        }

        return _registry.DeleteValue(WindowsUpdatePolicy.PauseKeyPath, WindowsUpdatePolicy.PauseStartValueName, out failure);
    }

    public bool SetDeferral(int featureDays, int qualityDays, out string? failure)
    {
        int? previousFeature = _registry.GetInt32(
            WindowsUpdatePolicy.DeferralKeyPath,
            WindowsUpdatePolicy.FeatureDeferValueName);

        if (!_registry.SetInt32(
            WindowsUpdatePolicy.DeferralKeyPath,
            WindowsUpdatePolicy.FeatureDeferValueName,
            WindowsUpdatePolicy.ClampFeatureDeferralDays(featureDays),
            out failure))
        {
            return false;
        }

        if (_registry.SetInt32(
            WindowsUpdatePolicy.DeferralKeyPath,
            WindowsUpdatePolicy.QualityDeferValueName,
            WindowsUpdatePolicy.ClampQualityDeferralDays(qualityDays),
            out failure))
        {
            return true;
        }

        // The two halves are shown, journaled and reverted as one setting, so applying only the
        // feature half would leave the page describing a machine that does not exist. Put it back,
        // including removing a value that was not there to begin with.
        if (previousFeature is int restore)
        {
            _registry.SetInt32(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName, restore, out _);
        }
        else
        {
            _registry.DeleteValue(WindowsUpdatePolicy.DeferralKeyPath, WindowsUpdatePolicy.FeatureDeferValueName, out _);
        }

        return false;
    }
}
