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
