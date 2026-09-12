using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;

namespace Upkeep.App.Tests.Fakes;

/// <summary>Returns whatever scans a test sets up, without touching the disk.</summary>
public sealed class FakeJunkScanner : IJunkScanner
{
    public List<JunkCategoryScan> UserScans { get; } = [];

    public Task<IReadOnlyList<JunkCategoryScan>> ScanUserCategoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JunkCategoryScan>>(UserScans);

    public Task<JunkCategoryScan> ScanCategoryAsync(JunkCategoryId categoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(UserScans.FirstOrDefault(scan => scan.CategoryId == categoryId) ?? JunkCategoryScan.Empty(categoryId));
}

/// <summary>Records the plan it was given and returns a canned outcome.</summary>
public sealed class FakeCleanupExecutor : ICleanupExecutor
{
    public CleanupPlan? ExecutedPlan { get; private set; }

    public CleanupOutcome Outcome { get; set; } = new()
    {
        SessionId = "20260912-200000-abcdef12",
        Categories = [],
    };

    public Task<CleanupOutcome> ExecuteAsync(CleanupPlan plan, IProgress<CleanupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ExecutedPlan = plan;
        return Task.FromResult(Outcome);
    }
}

/// <summary>Answers elevation however the test wants, and records what was asked of the helper.</summary>
public sealed class FakeElevationService : IElevationService
{
    private readonly Dictionary<JunkCategoryId, JunkCategoryScan> _systemScans = [];

    public ElevationResult Availability { get; set; } = new(ElevationStatus.Available);

    public List<HelperRequest> SentRequests { get; } = [];

    /// <summary>Whether the helper accepts a service start-type change.</summary>
    public bool ServiceChangeSucceeds { get; set; } = true;

    public string? ServiceChangeFailureCode { get; set; } = "windows_refused";

    public bool IsElevated => Availability.IsAvailable;

    public void SetSystemScan(JunkCategoryScan scan) => _systemScans[scan.CategoryId] = scan;

    public Task<ElevationResult> EnsureAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Availability);

    public Task<HelperResponse> SendAsync(HelperRequest request, CancellationToken cancellationToken = default)
    {
        SentRequests.Add(request);

        HelperResponse response = request switch
        {
            ScanJunkCategoryRequest scan when _systemScans.TryGetValue(scan.CategoryId, out var result) => new JunkScanResponse(result),
            ScanJunkCategoryRequest scan => new JunkScanResponse(JunkCategoryScan.Empty(scan.CategoryId)),
            SetServiceStartTypeRequest => ServiceChangeSucceeds
                ? new ServiceChangeResponse(true, null, null)
                : new ServiceChangeResponse(false, ServiceChangeFailureCode, null),
            _ => new HelperOkResponse(),
        };

        return Task.FromResult(response);
    }
}

/// <summary>
/// Echoes keys instead of translations, so assertions are about which string was chosen rather
/// than about its wording — the wording is the resource files' job, and they have their own tests.
/// </summary>
public sealed class FakeLocalizationService : ILocalizationService
{
    public string CurrentLanguage => "en-US";

    public string GetString(string key) => key;

    public string GetString(string key, params object[] arguments) => $"{key}({string.Join('|', arguments)})";
}

/// <summary>Keeps log lines in memory so a test can assert something was recorded.</summary>
public sealed class FakeAppLogger : IAppLogger
{
    public List<string> Lines { get; } = [];

    public string LogDirectory => Path.Combine(Path.GetTempPath(), "upkeep-fake-logs");

    public Task LogErrorAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        Lines.Add($"ERROR {message}");
        return Task.CompletedTask;
    }

    public Task LogWarningAsync(string message, CancellationToken cancellationToken = default)
    {
        Lines.Add($"WARN {message}");
        return Task.CompletedTask;
    }

    public Task LogInfoAsync(string message, CancellationToken cancellationToken = default)
    {
        Lines.Add($"INFO {message}");
        return Task.CompletedTask;
    }
}
