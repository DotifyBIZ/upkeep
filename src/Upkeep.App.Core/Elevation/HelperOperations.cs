using System.Diagnostics.CodeAnalysis;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Drivers;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Updates;

namespace Upkeep.App.Core.Elevation;

/// <summary>
/// Everything the elevated helper is able to do, as one surface. Named separately from the classes
/// that implement it so the dispatcher — which is where the decisions live — can be tested without
/// creating restore points or deleting anything on the machine running the tests.
/// </summary>
public interface IHelperOperations
{
    /// <summary>Measures one machine-wide junk category.</summary>
    JunkCategoryScan ScanCategory(JunkCategoryId categoryId, CancellationToken cancellationToken);

    /// <summary>Cleans one machine-wide junk category, re-deriving its own file list first.</summary>
    Task<CategoryOutcome> CleanCategoryAsync(JunkCategoryId categoryId, IReadOnlyList<string> approvedPaths, CancellationToken cancellationToken);

    /// <summary>Turns System Protection on if needed, then takes a restore point.</summary>
    Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken);

    /// <summary>
    /// Changes a service's start type, after re-checking for itself that the service is one Upkeep
    /// is willing to change.
    /// </summary>
    Task<ServiceChangeResult> SetServiceStartTypeAsync(string serviceName, ServiceStartType startType, CancellationToken cancellationToken);

    /// <summary>Asks Windows Update which drivers have something newer. Never installs (ADR-0009).</summary>
    DriverUpdateSearchResult SearchDriverUpdates(CancellationToken cancellationToken);

    /// <summary>Pauses Windows Update, or ends the pause when days is zero.</summary>
    WindowsUpdateChangeResult SetUpdatePause(int days);

    /// <summary>Sets how long feature and quality updates are held back.</summary>
    WindowsUpdateChangeResult SetUpdateDeferral(int featureDays, int qualityDays);
}

/// <summary>
/// The real implementation, wiring the helper's operations to the scanners and Windows tools. A
/// thin pass-through: every decision it could make has been pushed into the classes it calls, or
/// into <see cref="HelperDispatcher"/>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pass-through to the scanner, cleaner and System Restore; the decisions around them are tested via HelperDispatcher and SystemJunkCleaner.")]
public sealed class WindowsHelperOperations : IHelperOperations
{
    private readonly SystemJunkScanner _scanner;
    private readonly SystemJunkCleaner _cleaner;
    private readonly IRestorePointService _restorePoints;
    private readonly ServiceConfigurator _serviceConfigurator;
    private readonly IDriverUpdateSearch _driverSearch;
    private readonly IWindowsUpdateSettings _updateSettings;
    private readonly string? _shellUserProfilePath;

    public WindowsHelperOperations(
        SystemJunkScanner scanner,
        SystemJunkCleaner cleaner,
        IRestorePointService restorePoints,
        ServiceConfigurator serviceConfigurator,
        IDriverUpdateSearch driverSearch,
        IWindowsUpdateSettings updateSettings,
        string? shellUserProfilePath)
    {
        _scanner = scanner;
        _cleaner = cleaner;
        _restorePoints = restorePoints;
        _serviceConfigurator = serviceConfigurator;
        _driverSearch = driverSearch;
        _updateSettings = updateSettings;
        _shellUserProfilePath = shellUserProfilePath;
    }

    public JunkCategoryScan ScanCategory(JunkCategoryId categoryId, CancellationToken cancellationToken) =>
        _scanner.Scan(categoryId, _shellUserProfilePath, cancellationToken);

    public Task<CategoryOutcome> CleanCategoryAsync(JunkCategoryId categoryId, IReadOnlyList<string> approvedPaths, CancellationToken cancellationToken) =>
        _cleaner.CleanAsync(categoryId, approvedPaths, _shellUserProfilePath, cancellationToken);

    public Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken) =>
        _restorePoints.EnsureRestorePointAsync(description, cancellationToken);

    public Task<ServiceChangeResult> SetServiceStartTypeAsync(string serviceName, ServiceStartType startType, CancellationToken cancellationToken) =>
        _serviceConfigurator.SetStartTypeAsync(serviceName, startType, cancellationToken);

    public DriverUpdateSearchResult SearchDriverUpdates(CancellationToken cancellationToken) =>
        _driverSearch.Search(cancellationToken);

    public WindowsUpdateChangeResult SetUpdatePause(int days)
    {
        // Zero days means the user ended the pause rather than shortening it.
        bool succeeded = days <= 0
            ? _updateSettings.ResumeNow(out string? failure)
            : _updateSettings.Pause(days, out failure);

        return new WindowsUpdateChangeResult(succeeded, _updateSettings.Read(), failure);
    }

    public WindowsUpdateChangeResult SetUpdateDeferral(int featureDays, int qualityDays)
    {
        bool succeeded = _updateSettings.SetDeferral(featureDays, qualityDays, out string? failure);
        return new WindowsUpdateChangeResult(succeeded, _updateSettings.Read(), failure);
    }
}
