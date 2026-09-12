using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Storage;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>Answers elevation requests however the test needs, and records what was sent.</summary>
public sealed class FakeElevationService : IElevationService
{
    private readonly Queue<HelperResponse> _responses = new();

    public ElevationResult Availability { get; set; } = new(ElevationStatus.Available);

    public List<HelperRequest> SentRequests { get; } = [];

    public bool IsElevated => Availability.IsAvailable;

    /// <summary>Queues what the helper will answer next.</summary>
    public void EnqueueResponse(HelperResponse response) => _responses.Enqueue(response);

    public Task<ElevationResult> EnsureAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Availability);

    public Task<HelperResponse> SendAsync(HelperRequest request, CancellationToken cancellationToken = default)
    {
        SentRequests.Add(request);

        return Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : new HelperErrorResponse(HelperErrorCodes.UnsupportedOperation, request.GetType().Name));
    }
}

/// <summary>Reports whatever restore-point outcome the test wants, and remembers being asked.</summary>
public sealed class FakeRestorePointService : IRestorePointService
{
    public RestorePointResult Result { get; set; } = new(RestorePointStatus.Created, "Upkeep test restore point");

    public bool ProtectionEnabled { get; set; } = true;

    public List<string> Requests { get; } = [];

    public Task<bool> IsProtectionEnabledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtectionEnabled);

    public Task<RestorePointResult> EnsureRestorePointAsync(string description, CancellationToken cancellationToken = default)
    {
        Requests.Add(description);
        return Task.FromResult(Result);
    }
}

/// <summary>Reports fixed drive figures, so "now has X free" is deterministic.</summary>
public sealed class FakeDriveScanner : IDriveScanner
{
    public List<DriveSnapshot> Drives { get; } = [];

    public long? FreeBytes { get; set; } = 219L * 1024 * 1024 * 1024;

    public IReadOnlyList<DriveSnapshot> GetFixedDrives() => Drives;

    public long? GetFreeBytes(string path) => FreeBytes;
}
