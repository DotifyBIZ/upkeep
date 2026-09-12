using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Services;

namespace Upkeep.App.Core.Elevation;

/// <summary>
/// The elevated half of the app: this runs in the administrator process the shell starts once per
/// session (docs/adr/0005-elevated-helper-named-pipe.md). It owns the pipe — the elevated side is
/// the server, so a lower-privileged process can't sit on the name and wait for the shell to
/// connect to it — and it answers only the requests in <see cref="HelperRequest"/>.
/// <para>
/// Nothing here may touch WinUI, and nothing here may use ambient per-user state: %TEMP% and
/// HKEY_CURRENT_USER inside this process belong to whoever's credentials went into the UAC
/// prompt, which is not necessarily the person using the app.
/// </para>
/// <para>
/// This class is pipe and process plumbing only. Every decision it used to make now lives in
/// <see cref="HelperDispatcher"/> (which request becomes what work) and
/// <see cref="HelperStartupArguments"/> (what arguments are acceptable) — both of which are tested.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Named-pipe server and process lifetime for an elevated process; its decisions live in HelperDispatcher and HelperStartupArguments, which are tested.")]
public static class HelperHost
{
    /// <summary>How long the helper waits for the shell to connect before giving up and exiting.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!HelperStartupArguments.TryParse(args, out var startup, out string? parseError))
        {
            return Fail($"Bad helper arguments: {parseError}");
        }

        try
        {
            return RunAsync(startup).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return Fail($"The elevated helper stopped: {ex}");
        }
    }

    private static async Task<int> RunAsync(HelperStartupArguments startup)
    {
        using var logger = new FileAppLogger();
        using var shellExited = new CancellationTokenSource();

        Process? shell = TryGetShellProcess(startup.ParentProcessId);
        if (shell is null)
        {
            await logger.LogWarningAsync("Elevated helper started but the shell process was already gone.");
            return 2;
        }

        using (shell)
        {
            // The helper exists only to serve one shell. If that shell dies, so does this process —
            // an elevated orphan waiting on a pipe is exactly what nobody wants left behind.
            shell.EnableRaisingEvents = true;
            shell.Exited += (_, _) => shellExited.Cancel();
            if (shell.HasExited)
            {
                return 0;
            }

            var shellUser = NativeProcessIdentity.GetProcessUserSid(startup.ParentProcessId);

            var pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(new PipeAccessRule(shellUser, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
            pipeSecurity.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));

            // FirstPipeInstance + a single instance: if the name is already taken, creation fails
            // rather than silently joining something else's pipe.
            using var server = NamedPipeServerStreamAcl.Create(
                startup.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity);

            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(shellExited.Token);
            connectTimeout.CancelAfter(ConnectTimeout);

            try
            {
                await server.WaitForConnectionAsync(connectTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                await logger.LogWarningAsync("Elevated helper exited: the shell never connected.");
                return 3;
            }

            int clientProcessId = NativeProcessIdentity.GetPipeClientProcessId(server.SafePipeHandle);
            if (clientProcessId != startup.ParentProcessId)
            {
                await logger.LogErrorAsync($"Elevated helper refused a connection from process {clientProcessId}; expected {startup.ParentProcessId}.");
                return 4;
            }

            await ServeAsync(server, BuildDispatcher(shellUser, logger), logger, shellExited.Token);
            return 0;
        }
    }

    /// <summary>
    /// Everything the helper is able to do, built once per session. The profile path is resolved
    /// from the *shell's* owner rather than this process's identity — under over-the-shoulder
    /// elevation they are different accounts, and excluding the wrong one would mean cleaning the
    /// temp folder of the person actually using the machine.
    /// </summary>
    private static HelperDispatcher BuildDispatcher(SecurityIdentifier shellUser, FileAppLogger logger)
    {
        var paths = new WellKnownPaths();
        var scanner = new SystemJunkScanner(paths);
        var toolRunner = new WindowsToolRunner();
        var cleaner = new SystemJunkCleaner(scanner, toolRunner, paths, logger);
        IRestorePointService restorePoints = new SystemRestoreService(paths, logger);

        var serviceConfigurator = new ServiceConfigurator(new ServiceScanner(new RegistryProbe()), toolRunner, paths, logger);

        var operations = new WindowsHelperOperations(
            scanner,
            cleaner,
            restorePoints,
            serviceConfigurator,
            UserProfileResolver.TryGetProfilePath(shellUser));

        return new HelperDispatcher(operations, logger);
    }

    private static async Task ServeAsync(NamedPipeServerStream server, HelperDispatcher dispatcher, FileAppLogger logger, CancellationToken cancellationToken)
    {
        while (server.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            HelperRequest? request;
            try
            {
                request = await PipeFraming.ReadFrameAsync<HelperRequest>(server, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                // A frame we can't parse says nothing about which request it was, so there is no
                // id to answer against and no way to resynchronize the stream. Drop the session.
                await logger.LogErrorAsync("Elevated helper received an unreadable frame and closed the pipe.", ex, CancellationToken.None);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (request is null)
            {
                return;
            }

            var response = await dispatcher.DispatchAsync(request, cancellationToken);

            try
            {
                await PipeFraming.WriteFrameAsync(server, response, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                return;
            }
        }
    }

    private static Process? TryGetShellProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int Fail(string message)
    {
        // Best effort: this process has no UI, and the shell will see the broken pipe either way.
        try
        {
            using var logger = new FileAppLogger();
            logger.LogErrorAsync(message).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return 1;
    }
}

/// <summary>Parsed <c>--elevated-helper --pipe &lt;name&gt; --parent &lt;pid&gt;</c> arguments.</summary>
public sealed record HelperStartupArguments(string PipeName, int ParentProcessId)
{
    public const string PipeArgument = "--pipe";
    public const string ParentArgument = "--parent";

    public static bool TryParse(IReadOnlyList<string> args, out HelperStartupArguments startup, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? pipeName = null;
        int? parentProcessId = null;

        for (int i = 0; i < args.Count - 1; i++)
        {
            switch (args[i])
            {
                case PipeArgument:
                    pipeName = args[i + 1];
                    break;
                case ParentArgument when int.TryParse(args[i + 1], out int parsed):
                    parentProcessId = parsed;
                    break;
                default:
                    break;
            }
        }

        startup = new HelperStartupArguments(pipeName ?? string.Empty, parentProcessId ?? 0);

        // The pipe name is generated by the shell and only ever used as a name; reject anything
        // that isn't the shape we generate rather than passing it through to Windows.
        if (string.IsNullOrEmpty(pipeName) || pipeName.Length is < 8 or > 64 || !pipeName.All(char.IsAsciiLetterOrDigit))
        {
            error = "missing or malformed pipe name";
            return false;
        }

        if (parentProcessId is null or <= 0)
        {
            error = "missing parent process id";
            return false;
        }

        error = null;
        return true;
    }
}
