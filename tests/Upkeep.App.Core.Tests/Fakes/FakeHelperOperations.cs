using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Drivers;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Updates;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>Stands in for everything the elevated helper can do, so the dispatcher's decisions can
/// be tested without administrator rights.</summary>
public sealed class FakeHelperOperations : IHelperOperations
{
    public JunkCategoryScan ScanResult { get; set; } = JunkCategoryScan.Empty(JunkCategoryId.WindowsTemp);

    public CategoryOutcome CleanResult { get; set; } = new(JunkCategoryId.WindowsTemp, 0, 0, 0);

    public RestorePointResult RestorePointResult { get; set; } = new(RestorePointStatus.Created, "Upkeep");

    public ServiceChangeResult ServiceChange { get; set; } = ServiceChangeResult.Ok;

    public DriverUpdateSearchResult DriverSearch { get; set; } = new(true, []);

    public WindowsUpdateChangeResult UpdateChange { get; set; } = new(true, WindowsUpdateState.NotConfigured, null);

    /// <summary>Set to have an operation throw, to prove the dispatcher answers rather than crashes.</summary>
    public Exception? ThrowOnOperation { get; set; }

    public List<string> Calls { get; } = [];

    public JunkCategoryScan ScanCategory(JunkCategoryId categoryId, CancellationToken cancellationToken)
    {
        Calls.Add($"scan:{categoryId}");
        return ThrowOnOperation is not null ? throw ThrowOnOperation : ScanResult;
    }

    public Task<CategoryOutcome> CleanCategoryAsync(JunkCategoryId categoryId, IReadOnlyList<string> approvedPaths, CancellationToken cancellationToken)
    {
        Calls.Add($"clean:{categoryId}:{approvedPaths.Count}");
        return ThrowOnOperation is not null ? throw ThrowOnOperation : Task.FromResult(CleanResult);
    }

    public Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken)
    {
        Calls.Add($"restore:{description}");
        return ThrowOnOperation is not null ? throw ThrowOnOperation : Task.FromResult(RestorePointResult);
    }

    public Task<ServiceChangeResult> SetServiceStartTypeAsync(string serviceName, ServiceStartType startType, CancellationToken cancellationToken)
    {
        Calls.Add("service:" + serviceName + ":" + startType);
        return ThrowOnOperation is not null ? throw ThrowOnOperation : Task.FromResult(ServiceChange);
    }

    public DriverUpdateSearchResult SearchDriverUpdates(CancellationToken cancellationToken)
    {
        Calls.Add("search-drivers");
        return ThrowOnOperation is not null ? throw ThrowOnOperation : DriverSearch;
    }

    public WindowsUpdateChangeResult SetUpdatePause(int days)
    {
        Calls.Add("pause:" + days);
        return ThrowOnOperation is not null ? throw ThrowOnOperation : UpdateChange;
    }

    public WindowsUpdateChangeResult SetUpdateDeferral(int featureDays, int qualityDays)
    {
        Calls.Add("defer:" + featureDays + ":" + qualityDays);
        return ThrowOnOperation is not null ? throw ThrowOnOperation : UpdateChange;
    }
}

/// <summary>Returns canned results instead of starting DISM or PowerShell.</summary>
public sealed class FakeWindowsToolRunner : IWindowsToolRunner
{
    public List<(string FileName, IReadOnlyList<string> Arguments)> Invocations { get; } = [];

    public ToolResult Result { get; set; } = new(0, string.Empty);

    public Task<ToolResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Invocations.Add((fileName, arguments));
        return Task.FromResult(Result);
    }
}
