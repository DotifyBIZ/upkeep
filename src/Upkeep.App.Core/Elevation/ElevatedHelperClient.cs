using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Upkeep.App.Core.Logging;

namespace Upkeep.App.Core.Elevation;

/// <summary>Why an elevated operation couldn't happen, in terms the UI can explain.</summary>
public enum ElevationStatus
{
    /// <summary>The helper is running and connected.</summary>
    Available,

    /// <summary>The user dismissed the UAC prompt. A normal answer, not a failure.</summary>
    Declined,

    /// <summary>The helper could not be started or connected to.</summary>
    Unavailable,
}

public sealed record ElevationResult(ElevationStatus Status, string? Detail = null)
{
    public bool IsAvailable => Status == ElevationStatus.Available;
}

/// <summary>
/// The shell's side of the elevated helper (docs/adr/0005-elevated-helper-named-pipe.md). Starts
/// one helper per session on first use — one UAC prompt, not one per action — and keeps the pipe
/// open until the app exits.
/// <para>
/// Process launching and pipe connection can't be exercised without actually elevating, so this
/// class is excluded from the coverage bar; what it decides — the closed operation set, the frame
/// format, the refusal handling — lives in <see cref="HelperDispatcher"/>,
/// <see cref="PipeFraming"/> and the view models, which are tested.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Starts an elevated process and connects its pipe; the protocol and the decisions around it are tested separately.")]
public sealed class ElevatedHelperClient : IElevationService, IDisposable
{
    private readonly string _helperExecutablePath;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _helper;
    private NamedPipeClientStream? _pipe;
    private int _nextRequestId;
    private bool _userDeclined;

    public ElevatedHelperClient(string helperExecutablePath, IAppLogger logger)
    {
        _helperExecutablePath = helperExecutablePath;
        _logger = logger;
    }

    /// <summary>True once a helper is running and connected for this session.</summary>
    public bool IsElevated => _pipe?.IsConnected == true;

    public async Task<ElevationResult> EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (IsElevated)
        {
            return new ElevationResult(ElevationStatus.Available);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsElevated)
            {
                return new ElevationResult(ElevationStatus.Available);
            }

            // Once someone says no to the UAC prompt, asking again on the next click is nagging.
            // The user can retry deliberately; RetryElevation clears this.
            if (_userDeclined)
            {
                return new ElevationResult(ElevationStatus.Declined);
            }

            return await StartHelperAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Clears a previous refusal so an explicit "try again" can prompt once more.</summary>
    public void RetryElevation() => _userDeclined = false;

    public async Task<HelperResponse> SendAsync(HelperRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var availability = await EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            return new HelperErrorResponse(HelperErrorCodes.WindowsRefused, availability.Status.ToString());
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var addressed = request with { RequestId = Interlocked.Increment(ref _nextRequestId) };
            await PipeFraming.WriteFrameAsync(_pipe!, addressed, cancellationToken);
            var response = await PipeFraming.ReadFrameAsync<HelperResponse>(_pipe!, cancellationToken);

            return response ?? new HelperErrorResponse(HelperErrorCodes.WindowsRefused, "The elevated helper closed the connection.")
            {
                RequestId = addressed.RequestId,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            await _logger.LogErrorAsync("The elevated helper connection failed.", ex, cancellationToken);
            Disconnect();
            return new HelperErrorResponse(HelperErrorCodes.WindowsRefused, ex.Message) { RequestId = request.RequestId };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ElevationResult> StartHelperAsync(CancellationToken cancellationToken)
    {
        // Random per session: the name is not a secret (an elevated process's command line isn't
        // readable from medium integrity anyway), but a fixed name would let anything on the
        // machine sit on it first.
        string pipeName = "upkeep" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        int shellProcessId = Environment.ProcessId;

        var startInfo = new ProcessStartInfo(_helperExecutablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add("--elevated-helper");
        startInfo.ArgumentList.Add(HelperStartupArguments.PipeArgument);
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add(HelperStartupArguments.ParentArgument);
        startInfo.ArgumentList.Add(shellProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            _helper = Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — the user dismissed the UAC prompt.
            _userDeclined = true;
            await _logger.LogInfoAsync("The user declined the administrator prompt; elevated items were skipped.", cancellationToken);
            return new ElevationResult(ElevationStatus.Declined);
        }
        catch (Win32Exception ex)
        {
            await _logger.LogErrorAsync("Could not start the elevated helper.", ex, cancellationToken);
            return new ElevationResult(ElevationStatus.Unavailable, ex.Message);
        }

        if (_helper is null)
        {
            return new ElevationResult(ElevationStatus.Unavailable, "The elevated helper did not start.");
        }

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await pipe.ConnectAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            await pipe.DisposeAsync();
            await _logger.LogErrorAsync("The elevated helper started but never accepted a connection.", ex as Exception, cancellationToken);
            return new ElevationResult(ElevationStatus.Unavailable, ex.Message);
        }

        _pipe = pipe;
        return new ElevationResult(ElevationStatus.Available);
    }

    private void Disconnect()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        // Closing the pipe is what tells the helper to exit; it also watches this process, so an
        // abrupt end of the shell has the same effect.
        Disconnect();
        _helper?.Dispose();
        _gate.Dispose();
    }
}
