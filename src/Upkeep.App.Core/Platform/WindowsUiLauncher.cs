using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Upkeep.App.Core.Platform;

/// <summary>
/// Opens one of Windows' own settings pages or dialogs.
/// <para>
/// Upkeep links to these rather than reimplementing them (CLAUDE.md): the full visual-effects
/// surface, power settings and indexing options are Windows dialogs that already exist, are
/// already localized, and already do the right thing.
/// </para>
/// </summary>
public interface IWindowsUiLauncher
{
    /// <summary>Opens the target and returns immediately. False when Windows would not open it.</summary>
    bool Open(string target, string? arguments = null);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Hands a target to the shell; when a view model opens one is tested against a substitute.")]
public sealed class WindowsUiLauncher : IWindowsUiLauncher
{
    /// <summary>Windows' own "Performance Options" dialog — the full visual-effects list.</summary>
    public const string PerformanceOptions = "SystemPropertiesPerformance.exe";

    /// <summary>Settings &gt; System &gt; Power &amp; battery, where the power mode lives on modern PCs.</summary>
    public const string PowerSettings = "ms-settings:powersleep";

    /// <summary>Settings > System > Windows Update, where driver updates are actually installed.</summary>
    public const string WindowsUpdate = "ms-settings:windowsupdate";

    /// <summary>Device Manager, which owns driver rollback and keeps the previous package.</summary>
    public const string DeviceManager = "devmgmt.msc";

    /// <summary>The Indexing Options control panel, which is still the only place to pick locations.</summary>
    public const string IndexingOptionsCommand = "control.exe";

    /// <inheritdoc cref="IndexingOptionsCommand" />
    public const string IndexingOptionsArguments = "srchadmin.dll";

    public bool Open(string target, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        // UseShellExecute so ms-settings: URIs resolve the way they do from Start.
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
        };

        if (!string.IsNullOrEmpty(arguments))
        {
            startInfo.Arguments = arguments;
        }

        try
        {
            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // The user cancelling a UAC prompt lands here too, and is not an error worth a banner.
            return false;
        }
    }
}
