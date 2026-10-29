using Upkeep.App.Core.Elevation;

namespace Upkeep.App.Core.Performance;

/// <summary>How asking for Windows' startup timings went.</summary>
public enum BootPerformanceStatus
{
    /// <summary>Read, and in <see cref="BootPerformanceSource.Cached"/>.</summary>
    Read,

    /// <summary>Not read: doing so needs administrator approval, and the caller didn't ask for a prompt.</summary>
    NeedsApproval,

    /// <summary>The person said no to the administrator prompt.</summary>
    Declined,

    /// <summary>The helper couldn't read the log.</summary>
    Failed,
}

/// <summary>Windows' startup timings, read once per run and shared by every tab that shows them.</summary>
public interface IBootPerformanceSource
{
    /// <summary>What was read this run, or null before the first successful read.</summary>
    BootPerformance? Cached { get; }

    /// <summary>
    /// Reads the timings through the elevated helper. Without <paramref name="allowPrompt"/> it only
    /// reads when the helper is already running, so opening a page never raises a UAC prompt by
    /// itself.
    /// </summary>
    Task<BootPerformanceStatus> ReadAsync(bool allowPrompt, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BootPerformanceSource : IBootPerformanceSource
{
    private readonly IElevationService _elevation;

    public BootPerformanceSource(IElevationService elevation) => _elevation = elevation;

    public BootPerformance? Cached { get; private set; }

    public async Task<BootPerformanceStatus> ReadAsync(bool allowPrompt, CancellationToken cancellationToken = default)
    {
        if (Cached is not null)
        {
            return BootPerformanceStatus.Read;
        }

        if (!allowPrompt && !_elevation.IsElevated)
        {
            return BootPerformanceStatus.NeedsApproval;
        }

        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            return availability.Status == ElevationStatus.Declined ? BootPerformanceStatus.Declined : BootPerformanceStatus.Failed;
        }

        if (await _elevation.SendAsync(new ReadBootPerformanceRequest(), cancellationToken) is not BootPerformanceResponse response)
        {
            return BootPerformanceStatus.Failed;
        }

        Cached = response.Performance;
        return BootPerformanceStatus.Read;
    }
}
