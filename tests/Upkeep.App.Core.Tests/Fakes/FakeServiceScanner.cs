using Upkeep.App.Core.Services;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>Reports whatever services the test says this machine has.</summary>
public sealed class FakeServiceScanner : IServiceScanner
{
    public List<ServiceInfo> Services { get; } = [];

    public Task<IReadOnlyList<ServiceInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServiceInfo>>(Services);
}
