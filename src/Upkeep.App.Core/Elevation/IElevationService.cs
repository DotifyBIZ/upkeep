namespace Upkeep.App.Core.Elevation;

/// <summary>
/// How the rest of the app asks for administrator-only work without knowing how elevation is
/// arranged. One helper per session, one UAC prompt — see
/// docs/adr/0005-elevated-helper-named-pipe.md.
/// </summary>
public interface IElevationService
{
    /// <summary>True once the elevated helper is running and connected for this session.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Makes sure the helper is available, prompting for elevation if it isn't. Returns
    /// <see cref="ElevationStatus.Declined"/> — not an exception — when the user says no.
    /// </summary>
    Task<ElevationResult> EnsureAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs one operation in the elevated helper.</summary>
    Task<HelperResponse> SendAsync(HelperRequest request, CancellationToken cancellationToken = default);
}
