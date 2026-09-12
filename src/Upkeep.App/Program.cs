using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Services;

namespace Upkeep.App;

/// <summary>
/// The single entry point for both faces of this executable: the WinUI shell a user sees, and the
/// elevated helper process the shell starts on demand (docs/adr/0005-elevated-helper-named-pipe.md).
/// The helper path must return before any WinUI type is touched — it runs as administrator, and
/// loading a UI framework in that process would hand it an attack surface it has no use for.
/// <para>
/// This replaces the XAML-generated Main; the project sets DISABLE_XAML_GENERATED_MAIN for it.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>Command-line switch that turns this process into the elevated helper.</summary>
    public const string ElevatedHelperSwitch = "--elevated-helper";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], ElevatedHelperSwitch, StringComparison.Ordinal))
        {
            return HelperHost.Run(args);
        }

        // Before any window exists: XAML resolves x:Uid against the resource context in place when
        // a page first loads, so the language has to be settled here rather than after startup.
        StartupLanguage.Apply();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
