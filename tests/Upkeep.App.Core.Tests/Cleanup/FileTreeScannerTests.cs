using Upkeep.App.Core.Cleanup;

namespace Upkeep.App.Core.Tests.Cleanup;

public class FileTreeScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"upkeep-tree-{Guid.NewGuid():N}");
    private readonly DateTime _now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    public FileTreeScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteFile(string relativePath, int sizeBytes, TimeSpan? age = null)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(fullPath, _now - (age ?? TimeSpan.FromDays(30)));
        return fullPath;
    }

    [Fact]
    public void Scan_MissingFolder_IsEmptyRatherThanAnError()
    {
        var scan = FileTreeScanner.Scan(Path.Combine(_root, "never-existed"), TimeSpan.Zero, _now);

        Assert.Empty(scan.Items);
        Assert.Equal(0, scan.TotalBytes);
    }

    [Fact]
    public void Scan_EmptyOrWhitespaceRoot_IsEmpty() =>
        Assert.Empty(FileTreeScanner.Scan("   ", TimeSpan.Zero, _now).Items);

    [Fact]
    public void Scan_FindsFilesInNestedFolders_AndSumsTheirSize()
    {
        WriteFile("a.tmp", 100);
        WriteFile(Path.Combine("nested", "b.tmp"), 200);
        WriteFile(Path.Combine("nested", "deeper", "c.tmp"), 300);

        var scan = FileTreeScanner.Scan(_root, TimeSpan.Zero, _now);

        Assert.Equal(3, scan.Items.Count);
        Assert.Equal(600, scan.TotalBytes);
    }

    [Fact]
    public void Scan_LeavesRecentFilesAlone()
    {
        // Something running right now keeps its working files in %TEMP%; deleting them mid-install
        // is the classic way a cleaner does real damage.
        WriteFile("old.tmp", 100, age: TimeSpan.FromDays(3));
        WriteFile("in-use.tmp", 500, age: TimeSpan.FromMinutes(5));

        var scan = FileTreeScanner.Scan(_root, JunkCatalog.RecentTempFileGrace, _now);

        Assert.Single(scan.Items);
        Assert.EndsWith("old.tmp", scan.Items[0].Path, StringComparison.Ordinal);
        Assert.Equal(1, scan.SkippedCount);
    }

    [Fact]
    public void Scan_ZeroGracePeriod_IncludesEvenBrandNewFiles()
    {
        // A browser cache has no equivalent risk, so age is irrelevant there.
        WriteFile("fresh.bin", 50, age: TimeSpan.Zero);

        var scan = FileTreeScanner.Scan(_root, TimeSpan.Zero, _now);

        Assert.Single(scan.Items);
        Assert.Equal(0, scan.SkippedCount);
    }

    [Fact]
    public void Scan_IncludesHiddenFiles()
    {
        // Most of a cache's size is in hidden files; skipping them would under-report every
        // category it is used for.
        string hidden = WriteFile("hidden.tmp", 400);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var scan = FileTreeScanner.Scan(_root, TimeSpan.Zero, _now);

        Assert.Single(scan.Items);
        Assert.Equal(400, scan.TotalBytes);
    }

    [Fact]
    public void Scan_ReportsSizesPerItem()
    {
        WriteFile("one.tmp", 128);

        var scan = FileTreeScanner.Scan(_root, TimeSpan.Zero, _now);

        Assert.Equal(128, scan.Items[0].SizeBytes);
        Assert.False(scan.HadUnreadableEntries);
    }

    [Fact]
    public void Scan_Cancellation_StopsTheWalk()
    {
        for (int index = 0; index < 20; index++)
        {
            WriteFile($"file-{index}.tmp", 10);
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => FileTreeScanner.Scan(_root, TimeSpan.Zero, _now, cancelled.Token));
    }
}
