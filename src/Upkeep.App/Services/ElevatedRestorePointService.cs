using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Safety;

namespace Upkeep.App.Services;

/// <summary>
/// The shell's view of restore points: it can't create one itself (that needs administrator
/// rights), so it asks the elevated helper and translates the answer back.
/// <para>
/// If the user declined the administrator prompt, that comes back as
/// <see cref="RestorePointStatus.NotElevated"/> rather than as a failure — the run continues
/// without the parts that need rights, and the results screen says a restore point wasn't made.
/// </para>
/// </summary>
public sealed class ElevatedRestorePointService : IRestorePointService
{
    private readonly IElevationService _elevation;

    public ElevatedRestorePointService(IElevationService elevation) => _elevation = elevation;

    public async Task<bool> IsProtectionEnabledAsync(CancellationToken cancellationToken = default)
    {
        // Asking would mean raising a UAC prompt just to answer a question nothing acts on yet;
        // the answer that matters comes back with the restore point attempt itself.
        await Task.CompletedTask;
        return _elevation.IsElevated;
    }

    public async Task<RestorePointResult> EnsureRestorePointAsync(string description, CancellationToken cancellationToken = default)
    {
        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            return new RestorePointResult(RestorePointStatus.NotElevated);
        }

        var response = await _elevation.SendAsync(new CreateRestorePointRequest(description), cancellationToken);

        return response switch
        {
            RestorePointResponse point => new RestorePointResult(point.Status, point.Description, point.ProtectionWasEnabled),
            _ => new RestorePointResult(RestorePointStatus.Unavailable),
        };
    }
}
