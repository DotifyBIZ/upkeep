using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Cleanup;

public class JunkScannerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeWellKnownPaths _paths = new();
    private readonly FakeRecycleBin _recycleBin = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _paths.Dispose();
    }

    private JunkScanner CreateScanner(params string[] runningProcesses) =>
        new(_paths, _recycleBin, new FakeRunningProcesses(runningProcesses), new FixedTimeProvider(Now));

    private string CreateChromiumProfile(string browserRelativePath, string profileName)
    {
        string cache = Path.Combine(_paths.LocalAppData, browserRelativePath, profileName, "Cache", "Cache_Data");
        Directory.CreateDirectory(cache);
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.LocalAppData, browserRelativePath, profileName, "Preferences"), 10);
        return cache;
    }

    [Fact]
    public async Task ScanUserCategoriesAsync_ReturnsOneScanPerUserCategory()
    {
        var scans = await CreateScanner().ScanUserCategoriesAsync(CancellationToken.None);

        Assert.Equal(JunkCatalog.ForScope(JunkScope.User).Count(), scans.Count);
        Assert.All(scans, scan => Assert.Equal(JunkScope.User, JunkCatalog.Get(scan.CategoryId).Scope));
    }

    [Fact]
    public async Task ScanCategoryAsync_SystemScopedCategory_IsRefused()
    {
        // System categories belong to the elevated helper; the shell must never walk those paths.
        var scanner = CreateScanner();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => scanner.ScanCategoryAsync(JunkCategoryId.WindowsTemp, CancellationToken.None));
    }

    [Fact]
    public async Task ScanCategoryAsync_UserTemp_LeavesFilesFromTheLastDayAlone()
    {
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.UserTemp, "old.tmp"), 500, Now.AddDays(-5));
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.UserTemp, "installer-working-file.tmp"), 900, Now.AddMinutes(-10));

        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.UserTemp, CancellationToken.None);

        Assert.Equal(500, scan.TotalBytes);
        Assert.Equal(1, scan.SkippedCount);
    }

    [Fact]
    public async Task ScanCategoryAsync_BrowserCache_FindsEveryProfilesCache()
    {
        string edgeCache = CreateChromiumProfile(Path.Combine("Microsoft", "Edge", "User Data"), "Default");
        FakeWellKnownPaths.WriteFile(Path.Combine(edgeCache, "data_1"), 2048, Now);

        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.BrowserCache, CancellationToken.None);

        Assert.Equal(2048, scan.TotalBytes);
        Assert.Equal(JunkScanNote.None, scan.Note);
        Assert.Contains(scan.Details, detail => detail.StartsWith("Microsoft Edge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanCategoryAsync_BrowserCache_RunningBrowserIsLeftOutAndSaidSo()
    {
        // Fighting a live browser's file locks half-deletes a profile; skipping it and explaining
        // why is the honest option (and what the preview shows).
        string edgeCache = CreateChromiumProfile(Path.Combine("Microsoft", "Edge", "User Data"), "Default");
        FakeWellKnownPaths.WriteFile(Path.Combine(edgeCache, "data_1"), 4096, Now);

        var scan = await CreateScanner("msedge").ScanCategoryAsync(JunkCategoryId.BrowserCache, CancellationToken.None);

        Assert.Equal(0, scan.TotalBytes);
        Assert.Equal(JunkScanNote.BrowserRunning, scan.Note);
        Assert.Contains("Microsoft Edge", scan.Details);
    }

    [Fact]
    public async Task ScanCategoryAsync_BrowserCache_OneBrowserRunningStillScansTheOther()
    {
        string edgeCache = CreateChromiumProfile(Path.Combine("Microsoft", "Edge", "User Data"), "Default");
        FakeWellKnownPaths.WriteFile(Path.Combine(edgeCache, "data_1"), 4096, Now);
        string chromeCache = CreateChromiumProfile(Path.Combine("Google", "Chrome", "User Data"), "Default");
        FakeWellKnownPaths.WriteFile(Path.Combine(chromeCache, "data_1"), 1024, Now);

        var scan = await CreateScanner("msedge").ScanCategoryAsync(JunkCategoryId.BrowserCache, CancellationToken.None);

        Assert.Equal(1024, scan.TotalBytes);
        Assert.Equal(JunkScanNote.BrowserRunning, scan.Note);
    }

    [Fact]
    public async Task ScanCategoryAsync_BrowserCache_FirefoxProfilesAreIncluded()
    {
        string cache = Path.Combine(_paths.LocalAppData, "Mozilla", "Firefox", "Profiles", "abc.default-release", "cache2");
        Directory.CreateDirectory(cache);
        FakeWellKnownPaths.WriteFile(Path.Combine(cache, "entry"), 777, Now);

        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.BrowserCache, CancellationToken.None);

        Assert.Equal(777, scan.TotalBytes);
    }

    [Fact]
    public async Task ScanCategoryAsync_ThumbnailCache_TakesOnlyTheCacheDatabases()
    {
        string explorer = Path.Combine(_paths.LocalAppData, "Microsoft", "Windows", "Explorer");
        FakeWellKnownPaths.WriteFile(Path.Combine(explorer, "thumbcache_256.db"), 300, Now);
        FakeWellKnownPaths.WriteFile(Path.Combine(explorer, "iconcache_32.db"), 200, Now);
        // Explorer keeps other state in the same folder, and it is not ours to delete.
        FakeWellKnownPaths.WriteFile(Path.Combine(explorer, "ExplorerStartupLog.etl"), 999, Now);

        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.ThumbnailCache, CancellationToken.None);

        Assert.Equal(500, scan.TotalBytes);
        Assert.Equal(2, scan.ItemCount);
        Assert.DoesNotContain(scan.Items, item => item.Path.EndsWith(".etl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanCategoryAsync_ShaderCache_ReadsWindowsOwnD3DSCacheFolder()
    {
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.LocalAppData, "D3DSCache", "cache.bin"), 4096, Now);

        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.ShaderCache, CancellationToken.None);

        Assert.Equal(4096, scan.TotalBytes);
    }

    [Fact]
    public async Task ScanCategoryAsync_UserCrashDumps_SumsEveryReportFolder()
    {
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.LocalAppData, "CrashDumps", "app.dmp"), 100, Now);
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive", "r1", "Report.wer"), 20, Now);
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.LocalAppData, "Microsoft", "Windows", "WER", "ReportQueue", "r2", "Report.wer"), 30, Now);

        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.UserCrashDumps, CancellationToken.None);

        Assert.Equal(150, scan.TotalBytes);
        Assert.Equal(3, scan.ItemCount);
    }

    [Fact]
    public async Task ScanCategoryAsync_RecycleBin_ReportsWhatTheShellSaysRatherThanFilePaths()
    {
        _recycleBin.Info = new RecycleBinInfo(SizeBytes: 1_200_000, ItemCount: 37);

        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.RecycleBin, CancellationToken.None);

        Assert.Empty(scan.Items);
        Assert.Equal(1_200_000, scan.TotalBytes);
        Assert.Equal(37, scan.ItemCount);
    }

    [Fact]
    public async Task ScanCategoryAsync_NothingToClean_ReportsAnEmptyCategory()
    {
        var scan = await CreateScanner().ScanCategoryAsync(JunkCategoryId.ShaderCache, CancellationToken.None);

        Assert.True(scan.IsEmpty);
        Assert.Equal(0, scan.TotalBytes);
    }

    [Fact]
    public async Task ScanUserCategoriesAsync_Cancellation_StopsTheScan()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateScanner().ScanUserCategoriesAsync(cancelled.Token));
    }
}
