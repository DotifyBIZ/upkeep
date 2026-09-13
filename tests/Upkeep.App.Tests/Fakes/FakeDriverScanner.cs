using Upkeep.App.Core.Drivers;

namespace Upkeep.App.Tests.Fakes;

/// <summary>Reports whatever drivers the test says this PC has.</summary>
public sealed class FakeDriverScanner : IDriverScanner
{
    public List<DriverInfo> Drivers { get; } = [];

    public Task<IReadOnlyList<DriverInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DriverInfo>>(Drivers);
}
