using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Apps;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Files;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Settings;
using Upkeep.App.Core.Startup;
using Upkeep.App.Core.Storage;
using Upkeep.App.Services;
using Upkeep.App.ViewModels;

namespace Upkeep.App;

/// <summary>
/// Application-specific behaviour on top of the default Application class. Composition happens
/// here and nowhere else: every service and view model is registered in
/// <see cref="ConfigureServices"/>.
/// </summary>
public partial class App : Application
{
    /// <summary>The main application window. Used for dialogs, pickers and interop.</summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>The UI thread dispatcher, for marshalling work back from background scans.</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>The native window handle (HWND), for WinRT interop that needs InitializeWithWindow.</summary>
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>Resolves services and view models — registered once in <see cref="ConfigureServices"/>.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window = new MainWindow();
        Window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core services are stateless singletons.
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IAppSettingsService, FileAppSettingsService>();
        services.AddSingleton<IDriveScanner, DriveScanner>();

        // The language was settled in Program.Main, before any window existed; this reads the same
        // answer rather than resolving it again.
        services.AddSingleton<ILocalizationService>(_ => new LocalizationService(StartupLanguage.Resolve()));

        // One elevated helper per session (docs/adr/0005-elevated-helper-named-pipe.md) — the same
        // executable, re-launched with a switch, so the path is this process's own.
        services.AddSingleton<IElevationService>(provider => new ElevatedHelperClient(
            Environment.ProcessPath!,
            provider.GetRequiredService<IAppLogger>()));

        services.AddHttpClient();
        services.AddSingleton<IUpdateCheckService, GitHubUpdateCheckService>();

        // The cleanup chain: scan (here), plan, execute. Machine-wide work and restore points go
        // through the helper, which is why those two are the elevated implementations.
        services.AddSingleton<IWellKnownPaths, WellKnownPaths>();
        services.AddSingleton<IRecycleBin, RecycleBin>();
        services.AddSingleton<IRunningProcesses, RunningProcesses>();
        services.AddSingleton<IJunkScanner, JunkScanner>();
        services.AddSingleton<ISessionJournal, SessionJournal>();
        services.AddSingleton<ISessionReverter, SessionReverter>();
        services.AddSingleton<IQuarantineStore, QuarantineStore>();
        services.AddSingleton<IRestorePointService, ElevatedRestorePointService>();
        services.AddSingleton<ICleanupExecutor, CleanupExecutor>();

        // Files: three read-only scans, and one executor that removes what the user picked.
        services.AddSingleton<IFileIdentityReader, FileIdentityReader>();
        services.AddSingleton<IDuplicateFinder, DuplicateFinder>();
        services.AddSingleton<ILargeFileFinder, LargeFileFinder>();
        services.AddSingleton<IDiskUsageScanner, DiskUsageScanner>();
        services.AddSingleton<IFileActionExecutor, FileActionExecutor>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();

        // Apps: list, uninstall through the app's own uninstaller, then clear its leftovers.
        services.AddSingleton<IRegistryProbe, RegistryProbe>();
        services.AddSingleton<RegistryKeyBackupService>();
        services.AddSingleton<IInstalledAppScanner, InstalledAppScanner>();
        services.AddSingleton<IUninstallLauncher, UninstallLauncher>();
        services.AddSingleton<LeftoverScanner>();
        services.AddSingleton<IAppUninstaller, AppUninstaller>();
        services.AddSingleton<ILeftoverRemover, LeftoverRemover>();

        // Startup: read what runs at sign-in, and toggle it the way Task Manager does.
        services.AddSingleton<IWindowsToolRunner, WindowsToolRunner>();
        services.AddSingleton<IStartupItemScanner, StartupItemScanner>();
        services.AddSingleton<IStartupItemToggler, StartupItemToggler>();
        services.AddSingleton<IServiceScanner, ServiceScanner>();
        services.AddSingleton<IPerformanceSettings, PerformanceSettings>();
        services.AddSingleton<IWindowsUiLauncher, WindowsUiLauncher>();

        // View models are transient: a fresh instance per navigation.
        services.AddTransient<HomeViewModel>();
        services.AddTransient<CleanupViewModel>();
        services.AddTransient<FilesViewModel>();
        services.AddTransient<AppsViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<ServicesViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<HistoryViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The one handler that must never fail itself: it writes the raw exception with no
    /// dependencies first, then makes a best-effort attempt through the app logger.
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            string crashFile = Path.Combine(Path.GetTempPath(), $"upkeep-crash-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(crashFile, e.Exception?.ToString() ?? e.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            Services.GetRequiredService<IAppLogger>()
                .LogErrorAsync("Unhandled exception.", e.Exception)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }
}
