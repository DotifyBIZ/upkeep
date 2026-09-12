using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Upkeep.App.Core.Apps;

/// <summary>How an attempt to run an app's uninstaller ended.</summary>
public enum UninstallLaunchStatus
{
    /// <summary>The uninstaller ran and exited.</summary>
    Completed,

    /// <summary>The user dismissed the uninstaller's own UAC prompt.</summary>
    Declined,

    /// <summary>The uninstaller could not be started at all.</summary>
    Failed,
}

/// <param name="Status">What happened.</param>
/// <param name="ExitCode">The uninstaller's exit code, where there was one.</param>
/// <param name="Detail">Why it failed, for the log.</param>
public sealed record UninstallLaunchResult(UninstallLaunchStatus Status, int? ExitCode = null, string? Detail = null);

/// <summary>Runs an app's own uninstaller, or removes a Store package.</summary>
public interface IUninstallLauncher
{
    /// <summary>
    /// Runs <paramref name="command"/> and waits for it to finish. Uses ShellExecute so that an
    /// uninstaller whose manifest asks for administrator rights raises its *own* UAC prompt —
    /// Upkeep's helper is for Upkeep's own work, not for laundering someone else's elevation.
    /// </summary>
    Task<UninstallLaunchResult> RunUninstallerAsync(UninstallCommand command, CancellationToken cancellationToken = default);

    /// <summary>Removes a Store package for the current user through the package manager.</summary>
    Task<UninstallLaunchResult> RemovePackageAsync(string packageFullName, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Starts third-party uninstallers and the package manager; the decisions around them live in AppUninstaller, which is tested.")]
public sealed class UninstallLauncher : IUninstallLauncher
{
    public async Task<UninstallLaunchResult> RunUninstallerAsync(UninstallCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var startInfo = new ProcessStartInfo(command.FileName)
        {
            // The uninstaller is someone else's program with its own UI; it gets a window and its
            // own elevation prompt if it needs one.
            UseShellExecute = true,
        };

        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new UninstallLaunchResult(UninstallLaunchStatus.Failed, Detail: "The uninstaller did not start.");
            }

            await process.WaitForExitAsync(cancellationToken);
            return new UninstallLaunchResult(UninstallLaunchStatus.Completed, process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — the user said no to the uninstaller's own prompt.
            return new UninstallLaunchResult(UninstallLaunchStatus.Declined);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            return new UninstallLaunchResult(UninstallLaunchStatus.Failed, Detail: ex.Message);
        }
    }

    public async Task<UninstallLaunchResult> RemovePackageAsync(string packageFullName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFullName);

        try
        {
            var packageManager = new Windows.Management.Deployment.PackageManager();
            var operation = packageManager.RemovePackageAsync(packageFullName);
            var result = await operation.AsTask(cancellationToken);

            return result.IsRegistered
                ? new UninstallLaunchResult(UninstallLaunchStatus.Failed, Detail: result.ErrorText)
                : new UninstallLaunchResult(UninstallLaunchStatus.Completed, 0);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return new UninstallLaunchResult(UninstallLaunchStatus.Failed, Detail: ex.Message);
        }
    }
}
