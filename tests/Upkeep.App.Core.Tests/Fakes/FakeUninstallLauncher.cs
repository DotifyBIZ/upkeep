using Upkeep.App.Core.Apps;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>
/// Stands in for running somebody else's uninstaller. Records what it was asked to run, so tests
/// can check Upkeep launches the app's own uninstaller rather than deleting anything itself.
/// </summary>
public sealed class FakeUninstallLauncher : IUninstallLauncher
{
    public UninstallLaunchResult Result { get; set; } = new(UninstallLaunchStatus.Completed, 0);

    public List<UninstallCommand> LaunchedCommands { get; } = [];

    public List<string> RemovedPackages { get; } = [];

    /// <summary>Runs when an uninstall is launched — lets a test make the registry entry vanish.</summary>
    public Action? OnUninstall { get; set; }

    public Task<UninstallLaunchResult> RunUninstallerAsync(UninstallCommand command, CancellationToken cancellationToken = default)
    {
        LaunchedCommands.Add(command);
        OnUninstall?.Invoke();
        return Task.FromResult(Result);
    }

    public Task<UninstallLaunchResult> RemovePackageAsync(string packageFullName, CancellationToken cancellationToken = default)
    {
        RemovedPackages.Add(packageFullName);
        OnUninstall?.Invoke();
        return Task.FromResult(Result);
    }
}
