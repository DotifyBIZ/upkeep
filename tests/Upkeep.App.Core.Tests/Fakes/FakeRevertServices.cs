using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>Reports whatever startup items the test says this machine has.</summary>
public sealed class FakeStartupItemScanner : IStartupItemScanner
{
    public List<StartupItem> Items { get; } = [];

    public Task<IReadOnlyList<StartupItem>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StartupItem>>(Items);
}

/// <summary>
/// Records the toggles asked of it. The revert path uses the non-journaling call, so the two are
/// recorded separately — a revert that quietly wrote journal entries would be a defect.
/// </summary>
public sealed class FakeStartupItemToggler : IStartupItemToggler
{
    public bool Succeeds { get; set; } = true;

    /// <summary>Toggles made through the journaling path.</summary>
    public List<(string ItemId, bool Enabled)> JournaledChanges { get; } = [];

    /// <summary>Toggles made through the path a revert uses.</summary>
    public List<(string ItemId, bool Enabled)> DirectChanges { get; } = [];

    public Task<(SessionManifest Session, bool Success)> SetEnabledAsync(
        SessionManifest session,
        StartupItem item,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        JournaledChanges.Add((item.Id, enabled));
        return Task.FromResult((session, Succeeds));
    }

    public Task<(bool Success, string? Failure)> SetEnabledWithoutJournalAsync(
        StartupItem item,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        DirectChanges.Add((item.Id, enabled));
        string? failure = Succeeds ? null : "Windows refused it.";
        return Task.FromResult((Succeeds, failure));
    }
}

/// <summary>Records the performance settings a revert puts back.</summary>
public sealed class FakePerformanceSettings : IPerformanceSettings
{
    public PerformanceState State { get; set; } = new(AnimationsEnabled: true, TransparencyEnabled: true, PowerPlans: []);

    public bool Succeeds { get; set; } = true;

    public List<string> Changes { get; } = [];

    public PerformanceState Read() => State;

    public bool SetAnimationsEnabled(bool enabled, out string? failure) => Record($"animations={enabled}", out failure);

    public bool SetTransparencyEnabled(bool enabled, out string? failure) => Record($"transparency={enabled}", out failure);

    public bool SetActivePowerPlan(Guid planId, out string? failure) => Record($"power-plan={planId}", out failure);

    private bool Record(string change, out string? failure)
    {
        Changes.Add(change);
        failure = Succeeds ? null : "Windows refused it.";
        return Succeeds;
    }
}
