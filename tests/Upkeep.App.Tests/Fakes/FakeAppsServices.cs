using Upkeep.App.Core.Apps;

namespace Upkeep.App.Tests.Fakes;

/// <summary>Returns whatever app list a test sets up, without reading the real registry.</summary>
public sealed class FakeInstalledAppScanner : IInstalledAppScanner
{
    public List<InstalledApp> Apps { get; } = [];

    public Task<IReadOnlyList<InstalledApp>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstalledApp>>(Apps);
}

/// <summary>Pretends to run an uninstaller, and records which app it was asked about.</summary>
public sealed class FakeAppUninstaller : IAppUninstaller
{
    public UninstallOutcome Outcome { get; set; } = new(UninstallLaunchStatus.Completed, StillInstalled: false, []);

    public List<InstalledApp> UninstalledApps { get; } = [];

    public Task<UninstallOutcome> UninstallAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        UninstalledApps.Add(app);
        return Task.FromResult(Outcome);
    }
}

/// <summary>Records the leftovers it was asked to remove.</summary>
public sealed class FakeLeftoverRemover : ILeftoverRemover
{
    public LeftoverRemovalOutcome Outcome { get; set; } = new("20260912-210000-abcdef12", RemovedCount: 0, FailedCount: 0, QuarantinedBytes: 0);

    public List<LeftoverItem> RemovedItems { get; } = [];

    public Task<LeftoverRemovalOutcome> RemoveAsync(InstalledApp app, IReadOnlyList<LeftoverItem> items, CancellationToken cancellationToken = default)
    {
        RemovedItems.AddRange(items);
        return Task.FromResult(Outcome);
    }
}
