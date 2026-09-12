using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Apps;

/// <summary>What happened when an app was uninstalled, and what it left behind.</summary>
/// <param name="Status">How the app's own uninstaller ended.</param>
/// <param name="StillInstalled">
/// True when the app's registry entry is still there afterwards. Many uninstallers hand off to a
/// second process and exit immediately, so "the process finished" and "the app is gone" are
/// genuinely different questions.
/// </param>
/// <param name="Leftovers">What appears to remain — the user reviews this before anything else happens.</param>
public sealed record UninstallOutcome(
    UninstallLaunchStatus Status,
    bool StillInstalled,
    IReadOnlyList<LeftoverItem> Leftovers,
    string? Detail = null);

/// <summary>Removes an installed app, then looks for what it left behind.</summary>
public interface IAppUninstaller
{
    Task<UninstallOutcome> UninstallAsync(InstalledApp app, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the app's own uninstaller and then, only once that has finished, scans for remnants of
/// <em>that app</em> (README: no general registry cleaner).
/// <para>
/// Upkeep never removes an app by deleting its files: an installer knows about services, drivers,
/// scheduled tasks and shell extensions that a folder delete would strand. The leftover scan is
/// what happens after the uninstaller has had its turn.
/// </para>
/// </summary>
public sealed class AppUninstaller : IAppUninstaller
{
    /// <summary>How many times to re-check whether the entry disappeared after the process exits.</summary>
    private const int EntryRemovalChecks = 10;

    private readonly IUninstallLauncher _launcher;
    private readonly LeftoverScanner _leftoverScanner;
    private readonly IRegistryProbe _registry;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _entryPollInterval;

    /// <param name="entryPollInterval">
    /// Gap between checks. Counted in attempts rather than against a clock deadline on purpose: a
    /// deadline read from an injected clock while sleeping on the real one is a loop that never
    /// ends under a test clock.
    /// </param>
    public AppUninstaller(
        IUninstallLauncher launcher,
        LeftoverScanner leftoverScanner,
        IRegistryProbe registry,
        IAppLogger logger,
        TimeSpan? entryPollInterval = null)
    {
        _launcher = launcher;
        _leftoverScanner = leftoverScanner;
        _registry = registry;
        _logger = logger;
        _entryPollInterval = entryPollInterval ?? TimeSpan.FromSeconds(1);
    }

    public async Task<UninstallOutcome> UninstallAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var launch = app.Source == AppSource.Store
            ? await RemoveStorePackageAsync(app, cancellationToken)
            : await RunUninstallerAsync(app, cancellationToken);

        if (launch.Status != UninstallLaunchStatus.Completed)
        {
            await _logger.LogInfoAsync($"Uninstalling {app.DisplayName} ended as {launch.Status}: {launch.Detail}", cancellationToken);

            // Nothing was removed, so there is nothing to clean up after.
            return new UninstallOutcome(launch.Status, StillInstalled: true, [], launch.Detail);
        }

        bool stillInstalled = await WaitForEntryToDisappearAsync(app, cancellationToken);

        // Leftovers are only meaningful once the app itself is gone; offering to delete an
        // installed app's folders is how you break a working installation.
        var leftovers = stillInstalled
            ? []
            : _leftoverScanner.Scan(app, cancellationToken);

        return new UninstallOutcome(launch.Status, stillInstalled, leftovers, launch.Detail);
    }

    private async Task<UninstallLaunchResult> RunUninstallerAsync(InstalledApp app, CancellationToken cancellationToken)
    {
        if (app.Uninstall is null)
        {
            return new UninstallLaunchResult(UninstallLaunchStatus.Failed, Detail: "This app records no uninstaller.");
        }

        return await _launcher.RunUninstallerAsync(app.Uninstall, cancellationToken);
    }

    private async Task<UninstallLaunchResult> RemoveStorePackageAsync(InstalledApp app, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(app.PackageFullName))
        {
            return new UninstallLaunchResult(UninstallLaunchStatus.Failed, Detail: "This Store app records no package name.");
        }

        return await _launcher.RemovePackageAsync(app.PackageFullName, cancellationToken);
    }

    /// <summary>
    /// Many uninstallers copy themselves to a temporary folder, hand over and exit, so the process
    /// finishing tells you nothing. The registry entry disappearing does.
    /// </summary>
    private async Task<bool> WaitForEntryToDisappearAsync(InstalledApp app, CancellationToken cancellationToken)
    {
        if (app.Source == AppSource.Store || string.IsNullOrEmpty(app.RegistryKeyPath))
        {
            return false;
        }

        var hive = app.Scope == AppScope.AllUsers ? RegistryHiveName.LocalMachine : RegistryHiveName.CurrentUser;

        for (int attempt = 0; attempt < EntryRemovalChecks; attempt++)
        {
            if (!_registry.KeyExists(hive, app.RegistryKeyPath))
            {
                return false;
            }

            if (_entryPollInterval > TimeSpan.Zero)
            {
                await Task.Delay(_entryPollInterval, cancellationToken);
            }
        }

        return true;
    }
}
