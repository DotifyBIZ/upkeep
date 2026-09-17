using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Apps;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Drivers;
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
    private static Window? _window;
    private static Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private static IServiceProvider? _services;

    /// <summary>
    /// The main application window. Used for dialogs, pickers and interop.
    /// <para>
    /// Set in <see cref="OnLaunched"/>, which is the first thing the framework calls, so in
    /// practice this is never read before it exists. The throw is what "never" looks like when it
    /// happens anyway: a named mistake instead of a null reference three frames deeper.
    /// </para>
    /// </summary>
    public static Window Window =>
        _window ?? throw new InvalidOperationException("The main window does not exist until OnLaunched has run.");

    /// <summary>The UI thread dispatcher, for marshalling work back from background scans.</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue =>
        _dispatcherQueue ?? throw new InvalidOperationException("The dispatcher does not exist until OnLaunched has run.");

    /// <summary>The native window handle (HWND), for WinRT interop that needs InitializeWithWindow.</summary>
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>Resolves services and view models — registered once in <see cref="ConfigureServices"/>.</summary>
    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Services are not registered until the App constructor has run.");

    public App()
    {
        InitializeComponent();
        _services = ConfigureServices();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _window = new MainWindow();
        _window.Activate();
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
        // executable, re-launched with a switch, so the path is this process's own. Windows only
        // withholds that path for a process that no longer has an image on disk, which this one
        // plainly does — but "plainly" is not a null check, and the alternative is a crash at the
        // first elevation prompt rather than a named failure at startup.
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Windows did not report a path for the running executable.");

        services.AddSingleton<IElevationService>(provider => new ElevatedHelperClient(
            executablePath,
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
        services.AddSingleton<IDriverScanner, DriverScanner>();

        // Read once: the edition cannot change while the app is running.
        services.AddSingleton(provider => new WindowsEditionProbe(provider.GetRequiredService<IRegistryProbe>()).Read());

        // View models are transient: a fresh instance per navigation.
        services.AddTransient<HomeViewModel>();
        services.AddTransient<CleanupViewModel>();
        services.AddTransient<FilesViewModel>();
        services.AddTransient<AppsViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<ServicesViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DriversViewModel>();

        // Shell-level, not per-page: one palette lives for the whole run, same as the nav rail it stands in for.
        services.AddSingleton<CommandPaletteViewModel>();

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
            // Deliberately swallowed, and one of the few places that is right: the
            // crash file is a courtesy, and a handler that throws while handling a crash replaces
            // the real exception with its own. The app logger below is the second attempt.
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
            // Same reasoning, and nothing left to try: the crash file above was the fallback, and
            // the container may already be gone by the time an unhandled exception reaches here.
        }
    }
}
