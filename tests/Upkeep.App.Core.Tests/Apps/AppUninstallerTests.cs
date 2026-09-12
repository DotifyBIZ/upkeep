using Upkeep.App.Core.Apps;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Apps;

public class AppUninstallerTests : IDisposable
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Zoom";

    private readonly FakeWellKnownPaths _paths = new();
    private readonly FakeRegistryProbe _registry = new();
    private readonly FakeUninstallLauncher _launcher = new();
    private readonly FileAppLogger _logger;

    public AppUninstallerTests() =>
        _logger = new FileAppLogger(Path.Combine(Path.GetTempPath(), $"upkeep-uninstall-{Guid.NewGuid():N}"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _logger.Dispose();
        _paths.Dispose();

        try
        {
            Directory.Delete(_logger.LogDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private AppUninstaller CreateUninstaller() =>
        new(_launcher, new LeftoverScanner(_paths, _registry), _registry, _logger, TimeSpan.Zero);

    private static InstalledApp DesktopApp() => new()
    {
        Id = "Zoom",
        DisplayName = "Zoom Workplace",
        Publisher = "Zoom Communications",
        Source = AppSource.Desktop,
        Scope = AppScope.CurrentUser,
        RegistryKeyPath = UninstallKey,
        Uninstall = new UninstallCommand(@"C:\Program Files\Zoom\uninstall.exe", ["/S"]),
    };

    private static InstalledApp StoreApp() => new()
    {
        Id = "Contoso.App_1.0.0.0_x64__abc",
        DisplayName = "Contoso App",
        Source = AppSource.Store,
        Scope = AppScope.CurrentUser,
        PackageFullName = "Contoso.App_1.0.0.0_x64__abc",
    };

    [Fact]
    public async Task UninstallAsync_RunsTheAppsOwnUninstaller()
    {
        // Upkeep never removes an app by deleting files: the installer knows about services,
        // drivers and shell extensions a folder delete would strand.
        var outcome = await CreateUninstaller().UninstallAsync(DesktopApp(), CancellationToken.None);

        var command = Assert.Single(_launcher.LaunchedCommands);
        Assert.EndsWith("uninstall.exe", command.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UninstallLaunchStatus.Completed, outcome.Status);
    }

    [Fact]
    public async Task UninstallAsync_StoreApp_GoesThroughThePackageManager()
    {
        var outcome = await CreateUninstaller().UninstallAsync(StoreApp(), CancellationToken.None);

        Assert.Equal("Contoso.App_1.0.0.0_x64__abc", Assert.Single(_launcher.RemovedPackages));
        Assert.Empty(_launcher.LaunchedCommands);
        Assert.Equal(UninstallLaunchStatus.Completed, outcome.Status);
    }

    [Fact]
    public async Task UninstallAsync_EntryGone_ReportsTheAppAsRemoved()
    {
        var outcome = await CreateUninstaller().UninstallAsync(DesktopApp(), CancellationToken.None);

        Assert.False(outcome.StillInstalled);
    }

    [Fact]
    public async Task UninstallAsync_EntryStillThere_SaysSoRatherThanAssumingSuccess()
    {
        // Plenty of uninstallers hand off to a second process and exit immediately; "the process
        // finished" and "the app is gone" are different questions.
        _registry.AddKey(RegistryHiveName.CurrentUser, UninstallKey);

        var outcome = await CreateUninstaller().UninstallAsync(DesktopApp(), CancellationToken.None);

        Assert.True(outcome.StillInstalled);
    }

    [Fact]
    public async Task UninstallAsync_StillInstalled_OffersNoLeftovers()
    {
        // Offering to delete an installed app's folders is how a working installation gets broken.
        _registry.AddKey(RegistryHiveName.CurrentUser, UninstallKey);
        string folder = _paths.CreateUnder(Path.Combine("AppData", "Zoom"));
        FakeWellKnownPaths.WriteFile(Path.Combine(folder, "settings.json"), 10);

        var outcome = await CreateUninstaller().UninstallAsync(DesktopApp(), CancellationToken.None);

        Assert.Empty(outcome.Leftovers);
    }

    [Fact]
    public async Task UninstallAsync_Removed_FindsWhatTheAppLeftBehind()
    {
        string folder = _paths.CreateUnder(Path.Combine("AppData", "Zoom"));
        FakeWellKnownPaths.WriteFile(Path.Combine(folder, "settings.json"), 184);
        _registry.AddKey(RegistryHiveName.CurrentUser, @"Software\Zoom Workplace");

        var outcome = await CreateUninstaller().UninstallAsync(DesktopApp(), CancellationToken.None);

        Assert.Contains(outcome.Leftovers, item => item.Kind == LeftoverKind.Folder && item.Path == folder);
        Assert.Contains(outcome.Leftovers, item => item.Kind == LeftoverKind.RegistryKey);
    }

    [Fact]
    public async Task UninstallAsync_UserDeclinedTheUninstallersPrompt_ChangesNothing()
    {
        _launcher.Result = new UninstallLaunchResult(UninstallLaunchStatus.Declined);

        var outcome = await CreateUninstaller().UninstallAsync(DesktopApp(), CancellationToken.None);

        Assert.Equal(UninstallLaunchStatus.Declined, outcome.Status);
        Assert.True(outcome.StillInstalled);
        Assert.Empty(outcome.Leftovers);
    }

    [Fact]
    public async Task UninstallAsync_UninstallerFailedToStart_IsReportedNotThrown()
    {
        _launcher.Result = new UninstallLaunchResult(UninstallLaunchStatus.Failed, Detail: "File not found");

        var outcome = await CreateUninstaller().UninstallAsync(DesktopApp(), CancellationToken.None);

        Assert.Equal(UninstallLaunchStatus.Failed, outcome.Status);
        Assert.Equal("File not found", outcome.Detail);
    }

    [Fact]
    public async Task UninstallAsync_AppWithNoRecordedUninstaller_FailsCleanly()
    {
        var app = DesktopApp() with { Uninstall = null };

        var outcome = await CreateUninstaller().UninstallAsync(app, CancellationToken.None);

        Assert.Equal(UninstallLaunchStatus.Failed, outcome.Status);
        Assert.Empty(_launcher.LaunchedCommands);
    }

    [Fact]
    public async Task UninstallAsync_StoreAppWithNoPackageName_FailsCleanly()
    {
        var app = StoreApp() with { PackageFullName = null };

        var outcome = await CreateUninstaller().UninstallAsync(app, CancellationToken.None);

        Assert.Equal(UninstallLaunchStatus.Failed, outcome.Status);
        Assert.Empty(_launcher.RemovedPackages);
    }

    [Fact]
    public async Task UninstallAsync_MachineWideApp_ChecksTheMachineWideEntry()
    {
        var app = DesktopApp() with { Scope = AppScope.AllUsers };
        _registry.AddKey(RegistryHiveName.LocalMachine, UninstallKey);

        var outcome = await CreateUninstaller().UninstallAsync(app, CancellationToken.None);

        Assert.True(outcome.StillInstalled);
    }
}
