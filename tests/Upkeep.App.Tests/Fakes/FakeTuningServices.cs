using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Services;

namespace Upkeep.App.Tests.Fakes;

/// <summary>Reports whatever services the test says this machine has.</summary>
public sealed class FakeServiceScanner : IServiceScanner
{
    public List<ServiceInfo> Services { get; } = [];

    public Task<IReadOnlyList<ServiceInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServiceInfo>>(Services);
}

/// <summary>Reports a fixed performance state and records every change asked of it.</summary>
public sealed class FakePerformanceSettings : IPerformanceSettings
{
    public PerformanceState State { get; set; } = new(AnimationsEnabled: true, TransparencyEnabled: true, PowerPlans: []);

    /// <summary>Whether Windows accepts the change.</summary>
    public bool Succeeds { get; set; } = true;

    public string Failure { get; set; } = "Windows refused it.";

    public List<string> Changes { get; } = [];

    public PerformanceState Read() => State;

    public bool SetAnimationsEnabled(bool enabled, out string? failure) => Record($"animations={enabled}", out failure);

    public bool SetTransparencyEnabled(bool enabled, out string? failure) => Record($"transparency={enabled}", out failure);

    public bool SetActivePowerPlan(Guid planId, out string? failure) => Record($"power-plan={planId}", out failure);

    private bool Record(string change, out string? failure)
    {
        Changes.Add(change);
        failure = Succeeds ? null : Failure;
        return Succeeds;
    }
}

/// <summary>Records which Windows dialog the page asked for, without opening anything.</summary>
public sealed class FakeWindowsUiLauncher : IWindowsUiLauncher
{
    public List<(string Target, string? Arguments)> Opened { get; } = [];

    public bool Succeeds { get; set; } = true;

    public bool Open(string target, string? arguments = null)
    {
        Opened.Add((target, arguments));
        return Succeeds;
    }
}

/// <summary>Records power-mode changes instead of making them.</summary>
public sealed class FakePowerModeSettings : IPowerModeSettings
{
    public PowerModeState State { get; set; } = new(PowerMode.Balanced, PowerMode.Balanced, AppliesToActivePlan: true, HasBattery: true);

    public bool Succeeds { get; set; } = true;

    public List<string> Changes { get; } = [];

    public PowerModeState Read() => State;

    public bool SetMode(PowerSource source, PowerMode mode, out string? failure)
    {
        Changes.Add($"{source}={mode}");
        failure = Succeeds ? null : "Windows refused it.";
        return Succeeds;
    }
}

/// <summary>Records background-access changes instead of making them.</summary>
public sealed class FakeBackgroundAppSettings : IBackgroundAppSettings
{
    public List<BackgroundApp> Apps { get; } = [];

    public IReadOnlyList<RegistryValueSnapshot> CurrentValues { get; set; } = [];

    public bool Succeeds { get; set; } = true;

    public List<string> Changes { get; } = [];

    public IReadOnlyList<BackgroundApp> Read() => Apps;

    public IReadOnlyList<RegistryValueSnapshot> Snapshot(string familyName) => CurrentValues;

    public bool SetAllowed(string familyName, bool allowed, out string? failure)
    {
        Changes.Add($"{familyName}:allowed={allowed}");
        failure = Succeeds ? null : "Access is denied.";
        return Succeeds;
    }

    public bool Restore(string familyName, IReadOnlyList<RegistryValueSnapshot> previous, out string? failure)
    {
        Changes.Add($"{familyName}:restore");
        failure = null;
        return true;
    }
}
