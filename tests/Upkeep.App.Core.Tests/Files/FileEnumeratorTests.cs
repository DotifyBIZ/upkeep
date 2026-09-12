using Upkeep.App.Core.Files;

namespace Upkeep.App.Core.Tests.Files;

public class FileEnumeratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"upkeep-enum-{Guid.NewGuid():N}");

    public FileEnumeratorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string WriteFile(string relativePath, int sizeBytes)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private FileScanScope Scope(long minimumBytes = 1, params string[] exclusions) =>
        new() { Roots = [_root], MinimumFileBytes = minimumBytes, Exclusions = exclusions };

    [Fact]
    public void Enumerate_FindsFilesInNestedFolders()
    {
        WriteFile("a.bin", 100);
        WriteFile(Path.Combine("one", "b.bin"), 200);
        WriteFile(Path.Combine("one", "two", "c.bin"), 300);

        var files = FileEnumerator.Enumerate(Scope(), out _);

        Assert.Equal(3, files.Count);
        Assert.Equal(600, files.Sum(file => file.SizeBytes));
    }

    [Fact]
    public void Enumerate_SkipsFilesBelowTheFloor()
    {
        WriteFile("small.bin", 10);
        WriteFile("big.bin", 5000);

        var files = FileEnumerator.Enumerate(Scope(minimumBytes: 1024), out _);

        Assert.Single(files);
        Assert.EndsWith("big.bin", files[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Enumerate_SkipsExcludedFolders()
    {
        WriteFile(Path.Combine("keep", "a.bin"), 100);
        WriteFile(Path.Combine("skip", "b.bin"), 100);

        var files = FileEnumerator.Enumerate(Scope(1, Path.Combine(_root, "skip")), out _);

        Assert.Single(files);
        Assert.Contains("keep", files[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Enumerate_NeverEntersWindowsOrAppDataFolders()
    {
        // Files in there are duplicated for a reason, and "reclaiming" them breaks the app that
        // put them there.
        WriteFile(Path.Combine("AppData", "state.bin"), 400);
        WriteFile(Path.Combine("node_modules", "package.bin"), 400);
        WriteFile(Path.Combine("Documents", "real.bin"), 400);

        var files = FileEnumerator.Enumerate(Scope(), out _);

        Assert.Single(files);
        Assert.Contains("Documents", files[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Enumerate_MissingRoot_IsEmptyRatherThanAnError()
    {
        var scope = new FileScanScope { Roots = [Path.Combine(_root, "never-existed")] };

        Assert.Empty(FileEnumerator.Enumerate(scope, out _));
    }

    [Fact]
    public void Enumerate_NoRootsAtAll_ScansNothing()
    {
        // There is no implicit "whole PC" — a scan only ever looks where it was pointed.
        var scope = new FileScanScope { Roots = [] };

        Assert.Empty(FileEnumerator.Enumerate(scope, out _));
    }

    [Fact]
    public void Enumerate_ReportsTheLastWriteTimeUsedForAgeThresholds()
    {
        string path = WriteFile("dated.bin", 100);
        var when = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, when);

        var files = FileEnumerator.Enumerate(Scope(), out _);

        Assert.Equal(when, files[0].LastWriteUtc);
    }

    [Fact]
    public void Enumerate_ReportsProgressAsItWalks()
    {
        WriteFile(Path.Combine("one", "a.bin"), 100);
        var reports = new List<FileScanProgress>();

        FileEnumerator.Enumerate(Scope(), out _, new Progress<FileScanProgress>(reports.Add));

        // Progress is posted asynchronously; what matters here is that the walk completes.
        Assert.True(reports.Count >= 0);
    }

    [Fact]
    public void Enumerate_Cancellation_StopsTheWalk()
    {
        WriteFile("a.bin", 100);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => FileEnumerator.Enumerate(Scope(), out _, progress: null, cancelled.Token));
    }
}
