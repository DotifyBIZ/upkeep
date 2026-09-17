using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Drivers;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Storage;
using Upkeep.App.Core.Updates;

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

    /// <summary>Whether Windows Update could be searched at all.</summary>
    public bool DriverSearchSucceeded { get; set; } = true;

    public List<DriverUpdate> DriverUpdates { get; } = [];

    public bool UpdateChangeSucceeds { get; set; } = true;

    public WindowsUpdateState UpdateState { get; set; } = WindowsUpdateState.NotConfigured;

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
            SearchDriverUpdatesRequest => new DriverUpdateSearchResponse(DriverSearchSucceeded, DriverUpdates),
            SetUpdatePauseRequest or SetUpdateDeferralRequest =>
                new WindowsUpdateStateResponse(UpdateChangeSucceeds, UpdateState, UpdateChangeSucceeds ? null : "refused"),
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


/// <summary>
/// Answers the registry reads a view model makes to show what Windows is currently set to.
/// <para>
/// Reads are all a page does: writing the machine hive belongs to the elevated helper (ADR-0005),
/// so every write here reports a refusal rather than pretending to have succeeded.
/// </para>
/// </summary>
public sealed class FakeRegistryReader : IRegistryProbe
{
    private const string Refusal = "the shell does not write the registry in tests";

    /// <summary>String values, keyed by key path and value name.</summary>
    public Dictionary<(string KeyPath, string ValueName), string> Strings { get; } = [];

    /// <summary>DWORD values, keyed the same way.</summary>
    public Dictionary<(string KeyPath, string ValueName), int> Integers { get; } = [];

    public string? GetStringValue(RegistryHiveName hive, string keyPath, string valueName) =>
        Strings.GetValueOrDefault((keyPath, valueName));

    public int? GetInt32Value(RegistryHiveName hive, string keyPath, string valueName) =>
        Integers.TryGetValue((keyPath, valueName), out int value) ? value : null;

    public bool KeyExists(RegistryHiveName hive, string keyPath) => false;

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveName hive, string keyPath) => [];

    public IReadOnlyList<string> GetValueNames(RegistryHiveName hive, string keyPath) => [];

    public byte[]? GetBinaryValue(RegistryHiveName hive, string keyPath, string valueName) => null;

    public IReadOnlyList<RegistryValueSnapshot> GetValues(RegistryHiveName hive, string keyPath) => [];

    public bool CreateCurrentUserKey(string keyPath, out string? failure) => Refuse(out failure);

    public bool SetCurrentUserValue(string keyPath, RegistryValueSnapshot value, out string? failure) => Refuse(out failure);

    public bool SetCurrentUserBinaryValue(string keyPath, string valueName, byte[] value, out string? failure) => Refuse(out failure);

    public bool DeleteCurrentUserValue(string keyPath, string valueName, out string? failure) => Refuse(out failure);

    public bool DeleteCurrentUserKeyTree(string keyPath, out string? failure) => Refuse(out failure);

    private static bool Refuse(out string? failure)
    {
        failure = Refusal;
        return false;
    }
}

/// <summary>A clock that does not move, so "paused until" and "days left" are deterministic.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

/// <summary>Reports whatever drives a test put in it, without touching a real disk.</summary>
public sealed class FakeDriveScanner : IDriveScanner
{
    public List<DriveSnapshot> Drives { get; } = [];

    public long? FreeBytes { get; set; }

    public IReadOnlyList<DriveSnapshot> GetFixedDrives() => Drives;

    public long? GetFreeBytes(string path) => FreeBytes;
}
