using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Services;

public class ServiceConfiguratorTests : IDisposable
{
    private readonly FakeServiceScanner _scanner = new();
    private readonly FakeWindowsToolRunner _toolRunner = new();
    private readonly FakeWellKnownPaths _paths = new();
    private readonly FileAppLogger _logger;

    public ServiceConfiguratorTests() =>
        _logger = new FileAppLogger(Path.Combine(Path.GetTempPath(), $"upkeep-services-{Guid.NewGuid():N}"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _logger.Dispose();
        _paths.Dispose();
    }

    private static ServiceInfo Service(string name, bool hasRunningDependents = false) =>
        new(name, $"{name} display name", "Manual", IsRunning: false, IsMicrosoft: true, hasRunningDependents);

    private ServiceConfigurator CreateConfigurator() => new(_scanner, _toolRunner, _paths, _logger);

    [Fact]
    public async Task SetStartTypeAsync_CuratedService_AsksScToChangeIt()
    {
        _scanner.Services.Add(Service("DiagTrack"));

        var result = await CreateConfigurator().SetStartTypeAsync("DiagTrack", ServiceStartType.Disabled);

        Assert.True(result.Success);
        (string fileName, var arguments) = Assert.Single(_toolRunner.Invocations);
        Assert.EndsWith(@"System32\sc.exe", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["config", "DiagTrack", "start=", "disabled"], arguments);
    }

    [Fact]
    public async Task SetStartTypeAsync_ServiceWindowsNeeds_IsRefusedWithoutRunningAnything()
    {
        // The shell asking is not authority to do it: the helper classifies the service itself,
        // so a locked service stays locked whatever arrives over the pipe (ADR-0005).
        _scanner.Services.Add(Service("RpcSs"));

        var result = await CreateConfigurator().SetStartTypeAsync("RpcSs", ServiceStartType.Disabled);

        Assert.False(result.Success);
        Assert.Equal("service_locked", result.FailureCode);
        Assert.Empty(_toolRunner.Invocations);
    }

    [Fact]
    public async Task SetStartTypeAsync_ServiceSomethingRunningDependsOn_IsRefused()
    {
        // Stopping it stops whatever is running on top of it, whichever list it is on.
        _scanner.Services.Add(Service("SomeVendorSvc", hasRunningDependents: true));

        var result = await CreateConfigurator().SetStartTypeAsync("SomeVendorSvc", ServiceStartType.Manual);

        Assert.False(result.Success);
        Assert.Equal("service_locked", result.FailureCode);
        Assert.Empty(_toolRunner.Invocations);
    }

    [Fact]
    public async Task SetStartTypeAsync_ServiceThisMachineDoesNotHave_IsRefused()
    {
        var result = await CreateConfigurator().SetStartTypeAsync("NotInstalledSvc", ServiceStartType.Disabled);

        Assert.False(result.Success);
        Assert.Equal("service_not_found", result.FailureCode);
        Assert.Empty(_toolRunner.Invocations);
    }

    [Fact]
    public async Task SetStartTypeAsync_NameInADifferentCase_StillMatches()
    {
        // Windows treats service names case-insensitively; so does the re-check.
        _scanner.Services.Add(Service("DiagTrack"));

        var result = await CreateConfigurator().SetStartTypeAsync("diagtrack", ServiceStartType.Manual);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task SetStartTypeAsync_WindowsRefusedTheChange_SaysSoRatherThanClaimingSuccess()
    {
        _scanner.Services.Add(Service("DiagTrack"));
        _toolRunner.Result = new ToolResult(5, string.Empty);

        var result = await CreateConfigurator().SetStartTypeAsync("DiagTrack", ServiceStartType.Disabled);

        Assert.False(result.Success);
        Assert.Equal("windows_refused", result.FailureCode);
    }

    [Theory]
    [InlineData(ServiceStartType.Automatic, "auto")]
    [InlineData(ServiceStartType.AutomaticDelayed, "delayed-auto")]
    [InlineData(ServiceStartType.Manual, "demand")]
    [InlineData(ServiceStartType.Disabled, "disabled")]
    public void ToScValue_UsesScsOwnVocabulary(ServiceStartType startType, string expected)
    {
        // "demand" rather than "manual": getting these wrong means a service that silently keeps
        // the start type it had.
        Assert.Equal(expected, ServiceConfigurator.ToScValue(startType));
    }

    [Fact]
    public void ToScValue_StartTypeOutsideTheEnum_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceConfigurator.ToScValue((ServiceStartType)99));

    [Theory]
    [InlineData("Automatic", ServiceStartType.Automatic)]
    [InlineData("Disabled", ServiceStartType.Disabled)]
    [InlineData("Manual", ServiceStartType.Manual)]
    [InlineData("Unknown", ServiceStartType.Manual)]
    [InlineData(null, ServiceStartType.Manual)]
    public void FromReportedStartType_MapsWhatWindowsReported(string? reported, ServiceStartType expected) =>
        Assert.Equal(expected, ServiceConfigurator.FromReportedStartType(reported));
}
