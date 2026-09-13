using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Drivers;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Updates;
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

    [Fact]
    public async Task SearchDriverUpdatesRequest_ReturnsWhatWindowsUpdateOffered()
    {
        _operations.DriverSearch = new DriverUpdateSearchResult(
            true,
            [new DriverUpdate("Realtek Audio 6.0.9508.1", "Realtek High Definition Audio")]);

        var response = await CreateDispatcher().DispatchAsync(
            new SearchDriverUpdatesRequest { RequestId = 11 },
            CancellationToken.None);

        var search = Assert.IsType<DriverUpdateSearchResponse>(response);
        Assert.True(search.Succeeded);
        Assert.Equal("Realtek Audio 6.0.9508.1", Assert.Single(search.Updates).Title);
        Assert.Equal(11, response.RequestId);
        Assert.Equal("search-drivers", Assert.Single(_operations.Calls));
    }

    [Fact]
    public async Task SearchDriverUpdatesRequest_WindowsUpdateCouldNotBeAsked_IsAnAnswerNotAnError()
    {
        // A machine with the service off, or on a managed network, is a normal case.
        _operations.DriverSearch = DriverUpdateSearchResult.Unavailable;

        var response = await CreateDispatcher().DispatchAsync(new SearchDriverUpdatesRequest(), CancellationToken.None);

        var search = Assert.IsType<DriverUpdateSearchResponse>(response);
        Assert.False(search.Succeeded);
        Assert.Empty(search.Updates);
    }

    [Fact]
    public async Task SetUpdatePauseRequest_PassesTheDaysThroughAndReportsHowThingsStand()
    {
        _operations.UpdateChange = new WindowsUpdateChangeResult(
            true,
            new WindowsUpdateState(DateTimeOffset.UtcNow.AddDays(7), 0, 0),
            null);

        var response = await CreateDispatcher().DispatchAsync(
            new SetUpdatePauseRequest(7) { RequestId = 5 },
            CancellationToken.None);

        var state = Assert.IsType<WindowsUpdateStateResponse>(response);
        Assert.True(state.Success);
        Assert.True(state.State.IsPaused);
        Assert.Equal(5, response.RequestId);
        Assert.Equal("pause:7", Assert.Single(_operations.Calls));
    }

    [Fact]
    public async Task SetUpdateDeferralRequest_PassesBothPeriodsThrough()
    {
        var response = await CreateDispatcher().DispatchAsync(new SetUpdateDeferralRequest(180, 14), CancellationToken.None);

        Assert.IsType<WindowsUpdateStateResponse>(response);
        Assert.Equal("defer:180:14", Assert.Single(_operations.Calls));
    }

    [Fact]
    public async Task SetUpdatePauseRequest_WindowsRefused_SaysSoRatherThanClaimingSuccess()
    {
        _operations.UpdateChange = new WindowsUpdateChangeResult(false, WindowsUpdateState.NotConfigured, "Access is denied.");

        var response = await CreateDispatcher().DispatchAsync(new SetUpdatePauseRequest(7), CancellationToken.None);

        var state = Assert.IsType<WindowsUpdateStateResponse>(response);
        Assert.False(state.Success);
        Assert.Equal("Access is denied.", state.FailureDetail);
    }

    [Fact]
    public async Task DispatchAsync_OperationThrowsSomethingUnexpected_AnswersInsteadOfEndingTheProcess()
    {
        // An exception escaping here unwinds out of the pipe loop and ends the elevated process,
        // which drops the connection and costs the user a second elevation prompt mid-operation.
        _operations.ThrowOnOperation = new InvalidOperationException("the update agent fell over");

        var response = await CreateDispatcher().DispatchAsync(
            new SearchDriverUpdatesRequest { RequestId = 9 },
            CancellationToken.None);

        var error = Assert.IsType<HelperErrorResponse>(response);
        Assert.Equal(HelperErrorCodes.WindowsRefused, error.Code);
        Assert.Equal(9, error.RequestId);
    }

    [Fact]
    public async Task DispatchAsync_OperationIsCancelled_StillEndsTheSession()
    {
        // Cancellation is the helper shutting down, not an operation failing, so it must keep
        // unwinding rather than being answered as a refusal.
        _operations.ThrowOnOperation = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateDispatcher().DispatchAsync(new SearchDriverUpdatesRequest(), CancellationToken.None));
    }
}
