using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Storage;

namespace Upkeep.App.Core.Cleanup;

/// <summary>Carries out a cleanup plan the user has confirmed.</summary>
public interface ICleanupExecutor
{
    Task<CleanupOutcome> ExecuteAsync(CleanupPlan plan, IProgress<CleanupProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs a plan, in this order: open a session, take a restore point if the plan touches the
/// system, get elevation once if anything needs it, then work through the categories — user-scope
/// here, machine-wide through the helper.
/// <para>
/// Every category is journaled before it runs, so a session interrupted half-way still says what
/// it had started. Deleted caches are recorded as one summary entry per category rather than one
/// per file: a hundred thousand entries would make the manifest useless to read and slow to write,
/// and none of them are revertable anyway — the honest record is "this category, this much, gone".
/// </para>
/// </summary>
public sealed class CleanupExecutor : ICleanupExecutor
{
    private readonly ISessionJournal _journal;
    private readonly IRestorePointService _restorePoints;
    private readonly IElevationService _elevation;
    private readonly IRecycleBin _recycleBin;
    private readonly IDriveScanner _driveScanner;
    private readonly IWellKnownPaths _paths;
    private readonly IQuarantineStore _quarantine;
    private readonly IAppLogger _logger;

    public CleanupExecutor(
        ISessionJournal journal,
        IRestorePointService restorePoints,
        IElevationService elevation,
        IRecycleBin recycleBin,
        IDriveScanner driveScanner,
        IWellKnownPaths paths,
        IQuarantineStore quarantine,
        IAppLogger logger)
    {
        _journal = journal;
        _restorePoints = restorePoints;
        _elevation = elevation;
        _recycleBin = recycleBin;
        _driveScanner = driveScanner;
        _paths = paths;
        _quarantine = quarantine;
        _logger = logger;
    }

    public async Task<CleanupOutcome> ExecuteAsync(CleanupPlan plan, IProgress<CleanupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var session = await _journal.StartAsync(SessionKind.Cleanup, cancellationToken);
        var outcomes = new List<CategoryOutcome>();

        RestorePointResult? restorePoint = null;
        if (plan.NeedsRestorePoint)
        {
            restorePoint = await _restorePoints.EnsureRestorePointAsync(
                $"Upkeep cleanup, {DateTime.Now:g}",
                cancellationToken);

            session = await _journal.SaveAsync(
                session with
                {
                    RestorePointDescription = restorePoint.Description,
                    RestorePointWasReused = restorePoint.Status == RestorePointStatus.ReusedRecent,
                },
                cancellationToken);
        }

        // One prompt, before any work — so a run either has the rights it needs from the start or
        // proceeds without the parts that need them, rather than stopping half-way.
        bool elevationDeclined = false;
        if (plan.RequiresElevation)
        {
            var elevation = await _elevation.EnsureAvailableAsync(cancellationToken);
            elevationDeclined = !elevation.IsAvailable;

            if (elevationDeclined)
            {
                await _logger.LogInfoAsync("Cleanup continued without administrator rights; system-wide categories were skipped.", cancellationToken);
            }
        }

        int completed = 0;
        foreach (var planned in plan.Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new CleanupProgress(planned.Scan.CategoryId, completed, plan.Categories.Count));

            if (planned.Category.RequiresElevation && elevationDeclined)
            {
                outcomes.Add(new CategoryOutcome(planned.Scan.CategoryId, 0, 0, planned.ItemCount));
                completed++;
                continue;
            }

            (session, var outcome) = await RunCategoryAsync(session, planned, cancellationToken);
            outcomes.Add(outcome);
            completed++;
        }

        if (plan.Categories.Count > 0)
        {
            progress?.Report(new CleanupProgress(plan.Categories[^1].Scan.CategoryId, completed, plan.Categories.Count));
        }

        await _journal.SaveAsync(session with { CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);

        return new CleanupOutcome
        {
            SessionId = session.Id,
            Categories = outcomes,
            RestorePoint = restorePoint,
            ElevationDeclined = elevationDeclined,
            SystemDriveFreeBytes = _driveScanner.GetFreeBytes(_paths.SystemDriveRoot),
        };
    }

    private async Task<(SessionManifest Session, CategoryOutcome Outcome)> RunCategoryAsync(
        SessionManifest session,
        PlannedCategory planned,
        CancellationToken cancellationToken)
    {
        var category = planned.Category;

        // Quarantined categories are journaled a file at a time, because that is what it takes to
        // put each one back. Everything else is one summary entry per category: a deleted cache
        // has nothing to restore, and a hundred thousand entries would make the manifest useless.
        if (category.Removal == RemovalKind.Quarantined)
        {
            return await QuarantineCategoryAsync(session, planned, cancellationToken);
        }

        // Journaled before the work, not after: an interrupted run must still say what it started.
        var entry = new IrreversibleOperationEntry(
            $"Cleanup.{category.Id}",
            category.NameKey,
            planned.TotalBytes);

        session = await _journal.AppendAsync(session, entry, cancellationToken);
        int entryIndex = session.Entries.Count - 1;

        CategoryOutcome outcome;
        string? failure = null;

        try
        {
            outcome = category.Scope == JunkScope.System
                ? await CleanSystemCategoryAsync(planned, cancellationToken)
                : await CleanUserCategoryAsync(planned, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Cleaning {category.Id} failed.", ex, cancellationToken);
            failure = ex.Message;
            outcome = new CategoryOutcome(category.Id, 0, 0, planned.ItemCount);
        }

        session = await _journal.UpdateEntryAsync(
            session,
            entryIndex,
            entry with
            {
                Completed = failure is null,
                FailureDetail = failure,
                FreedBytesEstimate = outcome.FreedBytes,
            },
            cancellationToken);

        return (session, outcome);
    }

    /// <summary>
    /// Moves a category's files to quarantine, one journal entry each, written before the move.
    /// Nothing here is reported as freed: a quarantined file still occupies the disk until the
    /// quarantine is purged, and saying otherwise would be the "7.7 GB freed" that isn't.
    /// </summary>
    private async Task<(SessionManifest Session, CategoryOutcome Outcome)> QuarantineCategoryAsync(
        SessionManifest session,
        PlannedCategory planned,
        CancellationToken cancellationToken)
    {
        long affected = 0;
        long moved = 0;
        long skipped = 0;

        foreach (var item in planned.Scan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Size now, not the size the scan saw. A file that has since gone is one to skip.
            long size = TryGetSize(item.Path);
            if (size < 0)
            {
                skipped++;
                continue;
            }

            // The quarantine path isn't known until the move happens, which is why the entry is
            // written empty first and completed after — the record still exists if the run dies.
            session = await _journal.AppendAsync(session, new FileQuarantinedEntry(item.Path, string.Empty, size), cancellationToken);
            int entryIndex = session.Entries.Count - 1;

            var result = await _quarantine.QuarantineAsync(item.Path, session.Id, cancellationToken);
            if (result.Success)
            {
                affected += size;
                moved++;
            }
            else
            {
                skipped++;
                await _logger.LogWarningAsync($"Could not quarantine {item.Path}: {result.FailureDetail}", cancellationToken);
            }

            session = await _journal.UpdateEntryAsync(
                session,
                entryIndex,
                new FileQuarantinedEntry(item.Path, result.QuarantinePath ?? string.Empty, size)
                {
                    Completed = result.Success,
                    FailureDetail = result.FailureDetail,
                },
                cancellationToken);
        }

        await _logger.LogInfoAsync(
            $"Quarantined {moved} of {planned.ItemCount} files for {planned.Scan.CategoryId} ({affected} bytes held).",
            cancellationToken);

        return (session, new CategoryOutcome(planned.Scan.CategoryId, 0, moved, skipped));
    }

    private static long TryGetSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return -1;
        }
    }

    private async Task<CategoryOutcome> CleanUserCategoryAsync(PlannedCategory planned, CancellationToken cancellationToken)
    {
        if (planned.Scan.CategoryId == JunkCategoryId.RecycleBin)
        {
            // The shell owns this one; there are no paths to walk, and it is irreversible by
            // definition — which the preview said before the user confirmed.
            bool emptied = _recycleBin.Empty();
            return emptied
                ? new CategoryOutcome(JunkCategoryId.RecycleBin, planned.TotalBytes, planned.ItemCount, 0)
                : new CategoryOutcome(JunkCategoryId.RecycleBin, 0, 0, planned.ItemCount);
        }

        long freed = 0;
        long removed = 0;
        long skipped = 0;

        foreach (var item in planned.Scan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Size is re-read rather than trusted from the scan: a log file that grew between
                // the preview and now would otherwise be reported at its old size.
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
                // A file held open by something that started after the scan. Expected often enough
                // that it is a number on the results screen, not an error.
                skipped++;
            }
        }

        await Task.CompletedTask;
        return new CategoryOutcome(planned.Scan.CategoryId, freed, removed, skipped);
    }

    private async Task<CategoryOutcome> CleanSystemCategoryAsync(PlannedCategory planned, CancellationToken cancellationToken)
    {
        var response = await _elevation.SendAsync(
            new CleanJunkCategoryRequest(planned.Scan.CategoryId, [.. planned.Scan.Items.Select(item => item.Path)]),
            cancellationToken);

        switch (response)
        {
            case JunkCleanResponse clean:
                return new CategoryOutcome(planned.Scan.CategoryId, clean.FreedBytes, clean.ItemsRemoved, clean.ItemsSkipped);

            case HelperErrorResponse error:
                await _logger.LogWarningAsync($"The elevated helper refused {planned.Scan.CategoryId}: {error.Code} ({error.Detail})", cancellationToken);
                return new CategoryOutcome(planned.Scan.CategoryId, 0, 0, planned.ItemCount);

            default:
                return new CategoryOutcome(planned.Scan.CategoryId, 0, 0, planned.ItemCount);
        }
    }
}
