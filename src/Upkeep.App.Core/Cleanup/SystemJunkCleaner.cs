using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Cleanup;

/// <summary>
/// Cleans the machine-wide categories, inside the elevated helper.
/// <para>
/// This is where ADR-0005's re-derivation rule is enforced: whatever list of paths arrives with a
/// request, the cleaner runs <see cref="SystemJunkScanner"/> itself and removes only files that
/// appear in *both* sets. The request can therefore narrow what gets deleted — a user who
/// unticked something — but never widen it.
/// </para>
/// </summary>
public sealed class SystemJunkCleaner
{
    /// <summary>DISM rebuilds the component store; on a neglected machine this genuinely takes a while.</summary>
    private static readonly TimeSpan ComponentCleanupTimeout = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan CmdletTimeout = TimeSpan.FromMinutes(5);

    private readonly SystemJunkScanner _scanner;
    private readonly IWindowsToolRunner _toolRunner;
    private readonly IWellKnownPaths _paths;
    private readonly IAppLogger _logger;

    public SystemJunkCleaner(SystemJunkScanner scanner, IWindowsToolRunner toolRunner, IWellKnownPaths paths, IAppLogger logger)
    {
        _scanner = scanner;
        _toolRunner = toolRunner;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Cleans one category.
    /// </summary>
    /// <param name="categoryId">Which category — the only thing the request really decides.</param>
    /// <param name="approvedPaths">What the user saw in the preview. Used to narrow the helper's
    /// own scan, never to extend it.</param>
    /// <param name="excludeProfilePath">The profile belonging to whoever is using the shell.</param>
    public async Task<CategoryOutcome> CleanAsync(
        JunkCategoryId categoryId,
        IReadOnlyList<string> approvedPaths,
        string? excludeProfilePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvedPaths);

        var category = JunkCatalog.Get(categoryId);
        if (category.Scope != JunkScope.System)
        {
            throw new ArgumentOutOfRangeException(
                nameof(categoryId),
                categoryId,
                "The helper only cleans machine-wide categories; user-scope work belongs to the shell.");
        }

        return categoryId switch
        {
            JunkCategoryId.WindowsUpdateCleanup => await RunComponentStoreCleanupAsync(cancellationToken),
            JunkCategoryId.DeliveryOptimization => await ClearDeliveryOptimizationCacheAsync(cancellationToken),
            _ => DeleteDerivedFiles(categoryId, approvedPaths, excludeProfilePath, cancellationToken),
        };
    }

    private CategoryOutcome DeleteDerivedFiles(
        JunkCategoryId categoryId,
        IReadOnlyList<string> approvedPaths,
        string? excludeProfilePath,
        CancellationToken cancellationToken)
    {
        // The helper's own answer to "what is in this category" — computed here, not accepted.
        var derived = _scanner.Scan(categoryId, excludeProfilePath, cancellationToken);
        var approved = new HashSet<string>(approvedPaths, StringComparer.OrdinalIgnoreCase);

        long freed = 0;
        long removed = 0;
        long skipped = 0;

        foreach (var item in derived.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!approved.Contains(item.Path))
            {
                // Present on this machine but not in what the user approved: leave it.
                continue;
            }

            try
            {
                var info = new FileInfo(item.Path);
                if (!info.Exists)
                {
                    skipped++;
                    continue;
                }

                long size = info.Length;
                info.Delete();
                freed += size;
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // System temp is full of files something still holds open. Expected, and counted.
                skipped++;
            }
        }

        return new CategoryOutcome(categoryId, freed, removed, skipped);
    }

    /// <summary>
    /// Component store cleanup goes through DISM rather than any attempt to work out what is
    /// superseded ourselves — that judgement belongs to Windows, and getting it wrong means an
    /// unbootable machine. Deliberately without /ResetBase, which would make installed updates
    /// permanently uninstallable.
    /// </summary>
    private async Task<CategoryOutcome> RunComponentStoreCleanupAsync(CancellationToken cancellationToken)
    {
        string dism = Path.Combine(_paths.WindowsDirectory, "System32", "Dism.exe");
        var result = await _toolRunner.RunAsync(
            dism,
            ["/Online", "/Cleanup-Image", "/StartComponentCleanup", "/NoRestart"],
            ComponentCleanupTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            await _logger.LogWarningAsync($"DISM component cleanup returned {result.ExitCode}: {result.Output}", cancellationToken);
            return new CategoryOutcome(JunkCategoryId.WindowsUpdateCleanup, 0, 0, 1);
        }

        // DISM doesn't report bytes, and the freed space shows up as the drive's free space rather
        // than as a number here — the results screen reads that instead of inventing one.
        return new CategoryOutcome(JunkCategoryId.WindowsUpdateCleanup, 0, 1, 0);
    }

    /// <summary>
    /// Delivery Optimization has its own cmdlet for this. Clearing the folder by hand while the
    /// service holds it open half-works at best.
    /// </summary>
    private async Task<CategoryOutcome> ClearDeliveryOptimizationCacheAsync(CancellationToken cancellationToken)
    {
        long before = _scanner.Scan(JunkCategoryId.DeliveryOptimization, null, cancellationToken).TotalBytes;

        string powershell = Path.Combine(_paths.WindowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var result = await _toolRunner.RunAsync(
            powershell,
            ["-NoProfile", "-NonInteractive", "-Command", "Delete-DeliveryOptimizationCache -Force"],
            CmdletTimeout,
            cancellationToken);

        if (!result.Succeeded)
        {
            await _logger.LogWarningAsync($"Delete-DeliveryOptimizationCache returned {result.ExitCode}: {result.Output}", cancellationToken);
            return new CategoryOutcome(JunkCategoryId.DeliveryOptimization, 0, 0, 1);
        }

        long after = _scanner.Scan(JunkCategoryId.DeliveryOptimization, null, cancellationToken).TotalBytes;
        long freed = Math.Max(before - after, 0);

        return new CategoryOutcome(JunkCategoryId.DeliveryOptimization, freed, freed > 0 ? 1 : 0, 0);
    }
}
