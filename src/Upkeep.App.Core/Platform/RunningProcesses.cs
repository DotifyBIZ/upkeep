using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Upkeep.App.Core.Platform;

/// <summary>
/// Whether a given executable is running. Used to decide whether a browser's cache can be touched
/// at all — behind an interface so the decision that depends on it is testable without starting a
/// browser.
/// </summary>
public interface IRunningProcesses
{
    /// <summary>True if any process with this name (no extension, e.g. "msedge") is running.</summary>
    bool IsRunning(string processName);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin Process.GetProcessesByName wrapper; the logic that uses it is tested against a substitute.")]
public sealed class RunningProcesses : IRunningProcesses
{
    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            // GetProcessesByName hands back live handles; not disposing them leaks one per call,
            // and this is called on every scan.
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
