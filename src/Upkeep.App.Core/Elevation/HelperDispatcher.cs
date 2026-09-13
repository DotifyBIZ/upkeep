using Upkeep.App.Core.Logging;

namespace Upkeep.App.Core.Elevation;

/// <summary>
/// Turns one request into one response — the single place where a message from the shell becomes
/// work done with administrator rights.
/// <para>
/// It is a switch rather than a handler registry on purpose: what this process is willing to do
/// should be readable top to bottom in one review (ADR-0005). Anything not named here is refused,
/// and a refusal is a normal answer rather than an exception crossing the pipe.
/// </para>
/// </summary>
public sealed class HelperDispatcher
{
    private readonly IHelperOperations _operations;
    private readonly IAppLogger _logger;

    public HelperDispatcher(IHelperOperations operations, IAppLogger logger)
    {
        _operations = operations;
        _logger = logger;
    }

    public async Task<HelperResponse> DispatchAsync(HelperRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            switch (request)
            {
                case PingRequest:
                    return new HelperOkResponse { RequestId = request.RequestId };

                case ScanJunkCategoryRequest scan:
                    return new JunkScanResponse(_operations.ScanCategory(scan.CategoryId, cancellationToken))
                    {
                        RequestId = request.RequestId,
                    };

                case CleanJunkCategoryRequest clean:
                    {
                        var outcome = await _operations.CleanCategoryAsync(clean.CategoryId, clean.Paths, cancellationToken);
                        return new JunkCleanResponse(outcome.FreedBytes, outcome.ItemsRemoved, outcome.ItemsSkipped)
                        {
                            RequestId = request.RequestId,
                        };
                    }

                case SetServiceStartTypeRequest service:
                    {
                        var result = await _operations.SetServiceStartTypeAsync(service.ServiceName, service.StartType, cancellationToken);
                        return new ServiceChangeResponse(result.Success, result.FailureCode, result.Detail)
                        {
                            RequestId = request.RequestId,
                        };
                    }

                case SearchDriverUpdatesRequest:
                    {
                        var result = _operations.SearchDriverUpdates(cancellationToken);
                        return new DriverUpdateSearchResponse(result.Succeeded, result.Updates)
                        {
                            RequestId = request.RequestId,
                        };
                    }

                case SetUpdatePauseRequest pause:
                    {
                        var result = _operations.SetUpdatePause(pause.Days);
                        return new WindowsUpdateStateResponse(result.Success, result.State, result.FailureDetail)
                        {
                            RequestId = request.RequestId,
                        };
                    }

                case SetUpdateDeferralRequest defer:
                    {
                        var result = _operations.SetUpdateDeferral(defer.FeatureDays, defer.QualityDays);
                        return new WindowsUpdateStateResponse(result.Success, result.State, result.FailureDetail)
                        {
                            RequestId = request.RequestId,
                        };
                    }

                case CreateRestorePointRequest restorePoint:
                    {
                        var result = await _operations.CreateRestorePointAsync(restorePoint.Description, cancellationToken);
                        return new RestorePointResponse(result.Status, result.Description, result.ProtectionWasEnabled)
                        {
                            RequestId = request.RequestId,
                        };
                    }

                default:
                    await _logger.LogWarningAsync($"Elevated helper refused an unsupported operation: {request.GetType().Name}.", cancellationToken);
                    return new HelperErrorResponse(HelperErrorCodes.UnsupportedOperation, request.GetType().Name) { RequestId = request.RequestId };
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // A request for something outside what the operation is allowed to touch — a user-scope
            // category, an unknown id. Refused, not attempted.
            await _logger.LogWarningAsync($"Elevated helper refused {request.GetType().Name}: {ex.Message}", cancellationToken);
            return new HelperErrorResponse(HelperErrorCodes.RefusedByPolicy, ex.Message) { RequestId = request.RequestId };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Elevated helper could not complete {request.GetType().Name}.", ex, cancellationToken);
            return new HelperErrorResponse(HelperErrorCodes.WindowsRefused, ex.Message) { RequestId = request.RequestId };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Last resort. Anything unexpected from an operation — a COM failure from the update
            // agent, a WMI provider that will not answer, a bug in this process — used to unwind
            // past here and end the process, which drops the pipe and costs the user a second
            // elevation prompt in the middle of their work. One failed operation is one failed
            // response; the helper stays up and the shell hears why.
            await _logger.LogErrorAsync($"Elevated helper hit an unexpected failure in {request.GetType().Name}.", ex, CancellationToken.None);
            return new HelperErrorResponse(HelperErrorCodes.WindowsRefused, ex.Message) { RequestId = request.RequestId };
        }
    }
}
