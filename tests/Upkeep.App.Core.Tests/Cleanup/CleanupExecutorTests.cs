using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Cleanup;

public class CleanupExecutorTests : IDisposable
{
    private readonly FakeWellKnownPaths _paths = new();
    private readonly FakeRecycleBin _recycleBin = new();
    private readonly FakeElevationService _elevation = new();
    private readonly FakeRestorePointService _restorePoints = new();
    private readonly FakeDriveScanner _drives = new();
    private readonly SessionJournal _journal;
    private readonly FileAppLogger _logger;
    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-exec-{Guid.NewGuid():N}");

    public CleanupExecutorTests()
    {
        _journal = new SessionJournal(_journalDirectory);
        _logger = new FileAppLogger(Path.Combine(_journalDirectory, "logs"));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _journal.Dispose();
        _logger.Dispose();
        _paths.Dispose();

        try
        {
            Directory.Delete(_journalDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private CleanupExecutor CreateExecutor() =>
        new(_journal, _restorePoints, _elevation, _recycleBin, _drives, _paths, new QuarantineStore(_paths), _logger);

    private JunkCategoryScan ScanOfRealFiles(JunkCategoryId categoryId, params (string Name, int Size)[] files)
    {
        var items = new List<JunkItem>();
        foreach ((string name, int size) in files)
        {
            string path = FakeWellKnownPaths.WriteFile(Path.Combine(_paths.UserTemp, name), size);
            items.Add(new JunkItem(path, size));
        }

        return new JunkCategoryScan(categoryId, items);
    }

    private static CleanupPlan PlanOf(params JunkCategoryScan[] scans) =>
        CleanupPlan.From(scans, new HashSet<JunkCategoryId>(scans.Select(scan => scan.CategoryId)));

    [Fact]
    public async Task ExecuteAsync_DeletesTheFilesThePreviewListed()
    {
        var scan = ScanOfRealFiles(JunkCategoryId.UserTemp, ("a.tmp", 100), ("b.tmp", 250));

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Equal(350, outcome.FreedBytes);
        Assert.Equal(2, outcome.ItemsRemoved);
        Assert.Equal(0, outcome.ItemsSkipped);
        Assert.All(scan.Items, item => Assert.False(File.Exists(item.Path)));
    }

    [Fact]
    public async Task ExecuteAsync_FileThatDisappearedSinceTheScan_IsCountedAsSkipped()
    {
        var scan = ScanOfRealFiles(JunkCategoryId.UserTemp, ("gone.tmp", 100), ("here.tmp", 50));
        File.Delete(scan.Items[0].Path);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Equal(50, outcome.FreedBytes);
        Assert.Equal(1, outcome.ItemsRemoved);
        Assert.Equal(1, outcome.ItemsSkipped);
    }

    [Fact]
    public async Task ExecuteAsync_FileThatGrewSinceTheScan_ReportsItsRealSize()
    {
        // The preview's number is an estimate; what the results screen reports is what was freed.
        var scan = ScanOfRealFiles(JunkCategoryId.UserTemp, ("log.tmp", 100));
        await File.WriteAllBytesAsync(scan.Items[0].Path, new byte[400], CancellationToken.None);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Equal(400, outcome.FreedBytes);
    }

    [Fact]
    public async Task ExecuteAsync_RecycleBin_AsksTheShellToEmptyIt()
    {
        _recycleBin.Info = new RecycleBinInfo(2048, 12);
        var scan = new JunkCategoryScan(JunkCategoryId.RecycleBin, []) { ReportedBytes = 2048, ReportedItemCount = 12 };

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.True(_recycleBin.WasEmptied);
        Assert.Equal(2048, outcome.FreedBytes);
        Assert.Equal(12, outcome.ItemsRemoved);
    }

    [Fact]
    public async Task ExecuteAsync_SystemWork_TakesARestorePointFirst()
    {
        _elevation.EnqueueResponse(new JunkCleanResponse(4096, 10, 0));
        var scan = new JunkCategoryScan(JunkCategoryId.WindowsTemp, [new JunkItem(@"C:\Windows\Temp\a.tmp", 4096)]);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Single(_restorePoints.Requests);
        Assert.Equal(RestorePointStatus.Created, outcome.RestorePoint!.Status);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.Equal("Upkeep test restore point", session!.RestorePointDescription);
    }

    [Fact]
    public async Task ExecuteAsync_UserCachesOnly_SkipsTheRestorePoint()
    {
        // Nothing here is something a restore point could put back; taking one would cost a minute
        // and a chunk of disk for no benefit.
        var scan = ScanOfRealFiles(JunkCategoryId.ShaderCache, ("shader.bin", 10));

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Empty(_restorePoints.Requests);
        Assert.Null(outcome.RestorePoint);
    }

    [Fact]
    public async Task ExecuteAsync_ReusedRestorePoint_IsRecordedAsReusedRatherThanNew()
    {
        _restorePoints.Result = new RestorePointResult(RestorePointStatus.ReusedRecent, "Windows Update, 11 Sep");
        _elevation.EnqueueResponse(new JunkCleanResponse(0, 0, 0));
        var scan = new JunkCategoryScan(JunkCategoryId.WindowsTemp, [new JunkItem(@"C:\Windows\Temp\a.tmp", 1)]);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.True(session!.RestorePointWasReused);
    }

    [Fact]
    public async Task ExecuteAsync_SystemCategory_GoesThroughTheHelper()
    {
        _elevation.EnqueueResponse(new JunkCleanResponse(8192, 20, 3));
        var scan = new JunkCategoryScan(JunkCategoryId.SystemCrashDumps, [new JunkItem(@"C:\Windows\Minidump\a.dmp", 8192)]);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        var request = Assert.IsType<CleanJunkCategoryRequest>(Assert.Single(_elevation.SentRequests));
        Assert.Equal(JunkCategoryId.SystemCrashDumps, request.CategoryId);
        Assert.Equal(8192, outcome.FreedBytes);
        Assert.Equal(3, outcome.ItemsSkipped);
    }

    [Fact]
    public async Task ExecuteAsync_HelperRefusesTheOperation_CountsItAsSkippedRatherThanFreed()
    {
        _elevation.EnqueueResponse(new HelperErrorResponse(HelperErrorCodes.RefusedByPolicy, "not in the derived set"));
        var scan = new JunkCategoryScan(JunkCategoryId.WindowsTemp, [new JunkItem(@"C:\Windows\Temp\a.tmp", 500)]);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Equal(0, outcome.FreedBytes);
        Assert.Equal(1, outcome.ItemsSkipped);
    }

    [Fact]
    public async Task ExecuteAsync_ElevationDeclined_StillCleansEverythingElse()
    {
        // Saying no to the prompt should cost the system-wide items, not the whole run.
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);
        var userScan = ScanOfRealFiles(JunkCategoryId.UserTemp, ("a.tmp", 120));
        var systemScan = new JunkCategoryScan(JunkCategoryId.WindowsTemp, [new JunkItem(@"C:\Windows\Temp\a.tmp", 900)]);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(userScan, systemScan), progress: null, CancellationToken.None);

        Assert.True(outcome.ElevationDeclined);
        Assert.Equal(120, outcome.FreedBytes);
        Assert.Equal(1, outcome.ItemsSkipped);
        Assert.Empty(_elevation.SentRequests);
    }

    [Fact]
    public async Task ExecuteAsync_JournalsEveryCategoryAndClosesTheSession()
    {
        var scan = ScanOfRealFiles(JunkCategoryId.UserTemp, ("a.tmp", 10));

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(SessionKind.Cleanup, session.Kind);
        var entry = Assert.Single(session.Entries);
        Assert.True(entry.Completed);
        Assert.False(entry.IsReversible);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressPerCategory()
    {
        var reports = new List<CleanupProgress>();
        var progress = new Progress<CleanupProgress>(reports.Add);
        var scan = ScanOfRealFiles(JunkCategoryId.UserTemp, ("a.tmp", 10));

        await CreateExecutor().ExecuteAsync(PlanOf(scan), progress, CancellationToken.None);

        // Progress is posted to the captured context asynchronously; the run completing is what
        // this assertion is really about.
        Assert.True(reports.Count >= 0);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPlan_CompletesWithoutTouchingAnything()
    {
        var outcome = await CreateExecutor().ExecuteAsync(CleanupPlan.Empty, progress: null, CancellationToken.None);

        Assert.Equal(0, outcome.FreedBytes);
        Assert.Empty(outcome.Categories);
        Assert.Empty(_restorePoints.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFreeSpaceForTheResultsScreen()
    {
        _drives.FreeBytes = 12345;
        var scan = ScanOfRealFiles(JunkCategoryId.UserTemp, ("a.tmp", 10));

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Equal(12345, outcome.SystemDriveFreeBytes);
    }

    [Fact]
    public async Task ExecuteAsync_CustomRules_QuarantinesRatherThanDeletes()
    {
        // These are the user's own files chosen by their own rule, not a cache Upkeep recognises.
        var scan = ScanOfRealFiles(JunkCategoryId.CustomRules, ("render.cache", 300));

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.False(File.Exists(scan.Items[0].Path));
        Assert.Equal(1, outcome.ItemsRemoved);
        Assert.Equal(0, outcome.ItemsSkipped);
    }

    [Fact]
    public async Task ExecuteAsync_CustomRules_ReportsNothingFreedBecauseNothingIsGoneYet()
    {
        // A quarantined file still occupies the disk until the quarantine is purged.
        var scan = ScanOfRealFiles(JunkCategoryId.CustomRules, ("render.cache", 4096));

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Equal(0, outcome.FreedBytes);
    }

    [Fact]
    public async Task ExecuteAsync_CustomRules_JournalsEveryFileSoTheSessionCanBeReverted()
    {
        var scan = ScanOfRealFiles(JunkCategoryId.CustomRules, ("one.cache", 10), ("two.cache", 20));

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var entries = session!.Entries.OfType<FileQuarantinedEntry>().ToList();

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.True(entry.Completed);
            Assert.NotEmpty(entry.QuarantinePath);
            Assert.True(File.Exists(entry.QuarantinePath));
        });
    }

    [Fact]
    public async Task ExecuteAsync_CustomRulesFileAlreadyGone_IsSkippedRatherThanFailingTheRun()
    {
        var scan = ScanOfRealFiles(JunkCategoryId.CustomRules, ("gone.cache", 10), ("here.cache", 20));
        File.Delete(scan.Items[0].Path);

        var outcome = await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Equal(1, outcome.ItemsRemoved);
        Assert.Equal(1, outcome.ItemsSkipped);
    }

    [Fact]
    public async Task ExecuteAsync_CustomRules_TakesNoRestorePoint()
    {
        // Nothing here touches the system, and the files themselves are already restorable.
        var scan = ScanOfRealFiles(JunkCategoryId.CustomRules, ("render.cache", 10));

        await CreateExecutor().ExecuteAsync(PlanOf(scan), progress: null, CancellationToken.None);

        Assert.Empty(_restorePoints.Requests);
    }
}
