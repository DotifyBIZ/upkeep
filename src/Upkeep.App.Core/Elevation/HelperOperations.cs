using System.Diagnostics.CodeAnalysis;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Safety;

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
    private readonly string? _shellUserProfilePath;

    public WindowsHelperOperations(
        SystemJunkScanner scanner,
        SystemJunkCleaner cleaner,
        IRestorePointService restorePoints,
        string? shellUserProfilePath)
    {
        _scanner = scanner;
        _cleaner = cleaner;
        _restorePoints = restorePoints;
        _shellUserProfilePath = shellUserProfilePath;
    }

    public JunkCategoryScan ScanCategory(JunkCategoryId categoryId, CancellationToken cancellationToken) =>
        _scanner.Scan(categoryId, _shellUserProfilePath, cancellationToken);

    public Task<CategoryOutcome> CleanCategoryAsync(JunkCategoryId categoryId, IReadOnlyList<string> approvedPaths, CancellationToken cancellationToken) =>
        _cleaner.CleanAsync(categoryId, approvedPaths, _shellUserProfilePath, cancellationToken);

    public Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken) =>
        _restorePoints.EnsureRestorePointAsync(description, cancellationToken);
}
