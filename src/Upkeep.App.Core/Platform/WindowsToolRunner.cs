using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Upkeep.App.Core.Platform;

/// <summary>What a Windows tool reported when Upkeep ran it.</summary>
/// <param name="ExitCode">The process exit code, or -1 if it never started or had to be killed.</param>
/// <param name="Output">Standard output and error combined, for the diagnostic log.</param>
public sealed record ToolResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs one of Windows' own maintenance tools. Upkeep prefers these over reimplementing what they
/// do (CLAUDE.md): DISM decides what in the component store is superseded, and Delivery
/// Optimization has a cmdlet for clearing its own cache.
/// </summary>
public interface IWindowsToolRunner
{
    Task<ToolResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Starts real Windows tools; the callers' decisions are tested against a substitute.")]
public sealed class WindowsToolRunner : IWindowsToolRunner
{
    public async Task<ToolResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(fileName)
        {
            // No shell, and arguments passed as a list rather than a command line: nothing here is
            // ever built by string concatenation, so nothing can be smuggled through quoting.
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return new ToolResult(-1, $"{fileName} did not start.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ToolResult(-1, ex.Message);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            string output = string.Join(Environment.NewLine, await outputTask, await errorTask);
            return new ToolResult(process.ExitCode, output.Trim());
        }
        catch (OperationCanceledException)
        {
            // DISM in particular can sit for minutes; a tool that outlives its timeout is killed
            // rather than left running with nothing watching it.
            TryKill(process);
            return new ToolResult(-1, $"{fileName} did not finish within {timeout.TotalMinutes:0.#} minutes.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
