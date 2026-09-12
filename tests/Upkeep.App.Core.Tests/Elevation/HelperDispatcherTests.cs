using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Elevation;

public class HelperDispatcherTests : IDisposable
{
    private readonly FakeHelperOperations _operations = new();
    private readonly FileAppLogger _logger = new(Path.Combine(Path.GetTempPath(), $"upkeep-dispatch-{Guid.NewGuid():N}"));

    private HelperDispatcher CreateDispatcher() => new(_operations, _logger);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _logger.Dispose();
        try
        {
            Directory.Delete(_logger.LogDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Ping_IsAnsweredWithoutDoingAnything()
    {
        var response = await CreateDispatcher().DispatchAsync(new PingRequest { RequestId = 3 }, CancellationToken.None);

        Assert.IsType<HelperOkResponse>(response);
        Assert.Equal(3, response.RequestId);
        Assert.Empty(_operations.Calls);
    }

    [Fact]
    public async Task ScanRequest_ReturnsWhatTheHelperMeasured()
    {
        _operations.ScanResult = new JunkCategoryScan(JunkCategoryId.WindowsTemp, [new JunkItem(@"C:\Windows\Temp\a.tmp", 512)]);

        var response = await CreateDispatcher().DispatchAsync(
            new ScanJunkCategoryRequest(JunkCategoryId.WindowsTemp) { RequestId = 7 },
            CancellationToken.None);

        var scan = Assert.IsType<JunkScanResponse>(response);
        Assert.Equal(512, scan.Scan.TotalBytes);
        Assert.Equal(7, scan.RequestId);
    }

    [Fact]
    public async Task CleanRequest_PassesTheApprovedPathsThroughAndReportsTheOutcome()
    {
        _operations.CleanResult = new CategoryOutcome(JunkCategoryId.WindowsTemp, 2048, 5, 2);

        var response = await CreateDispatcher().DispatchAsync(
            new CleanJunkCategoryRequest(JunkCategoryId.WindowsTemp, [@"C:\Windows\Temp\a.tmp", @"C:\Windows\Temp\b.tmp"]),
            CancellationToken.None);

        var clean = Assert.IsType<JunkCleanResponse>(response);
        Assert.Equal(2048, clean.FreedBytes);
        Assert.Equal(5, clean.ItemsRemoved);
        Assert.Equal(2, clean.ItemsSkipped);
        Assert.Contains("clean:WindowsTemp:2", _operations.Calls);
    }

    [Fact]
    public async Task RestorePointRequest_ReportsWhatWindowsDid()
    {
        _operations.RestorePointResult = new RestorePointResult(RestorePointStatus.ReusedRecent, "Windows Update", ProtectionWasEnabled: true);

        var response = await CreateDispatcher().DispatchAsync(new CreateRestorePointRequest("Upkeep cleanup"), CancellationToken.None);

        var point = Assert.IsType<RestorePointResponse>(response);
        Assert.Equal(RestorePointStatus.ReusedRecent, point.Status);
        Assert.Equal("Windows Update", point.Description);
        Assert.True(point.ProtectionWasEnabled);
    }

    [Fact]
    public async Task OperationOutsideWhatIsAllowed_IsRefusedRatherThanAttempted()
    {
        // A user-scope category, an unknown id: the operation says no, and the dispatcher turns
        // that into an answer the shell can explain.
        _operations.ThrowOnOperation = new ArgumentOutOfRangeException("categoryId", "User-scope categories belong to the shell.");

        var response = await CreateDispatcher().DispatchAsync(
            new ScanJunkCategoryRequest(JunkCategoryId.UserTemp),
            CancellationToken.None);

        var error = Assert.IsType<HelperErrorResponse>(response);
        Assert.Equal(HelperErrorCodes.RefusedByPolicy, error.Code);
    }

    [Fact]
    public async Task WindowsRefusingTheWork_ComesBackAsAnErrorCodeNotACrash()
    {
        _operations.ThrowOnOperation = new UnauthorizedAccessException("Access is denied.");

        var response = await CreateDispatcher().DispatchAsync(
            new CleanJunkCategoryRequest(JunkCategoryId.WindowsTemp, []),
            CancellationToken.None);

        var error = Assert.IsType<HelperErrorResponse>(response);
        Assert.Equal(HelperErrorCodes.WindowsRefused, error.Code);
    }

    [Fact]
    public async Task IoFailure_ComesBackAsAnErrorCode()
    {
        _operations.ThrowOnOperation = new IOException("The file is in use.");

        var response = await CreateDispatcher().DispatchAsync(
            new ScanJunkCategoryRequest(JunkCategoryId.SystemCrashDumps),
            CancellationToken.None);

        Assert.Equal(HelperErrorCodes.WindowsRefused, Assert.IsType<HelperErrorResponse>(response).Code);
    }

    [Fact]
    public async Task RequestIdIsAlwaysEchoed_SoTheShellCanMatchTheAnswer()
    {
        _operations.ThrowOnOperation = new IOException("nope");

        var response = await CreateDispatcher().DispatchAsync(
            new CleanJunkCategoryRequest(JunkCategoryId.WindowsTemp, []) { RequestId = 42 },
            CancellationToken.None);

        Assert.Equal(42, response.RequestId);
    }
}
