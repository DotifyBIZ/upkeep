using Upkeep.App.Core.Files;

namespace Upkeep.App.Core.Tests.Files;

public class DiskUsageScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"upkeep-usage-{Guid.NewGuid():N}");

    public DiskUsageScannerTests() => Directory.CreateDirectory(_root);

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

    private void WriteFile(string relativePath, int sizeBytes)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
    }

    [Fact]
    public async Task ScanAsync_RollsFolderSizesUpTheTree()
    {
        WriteFile("a.bin", 100);
        WriteFile(Path.Combine("nested", "b.bin"), 200);
        WriteFile(Path.Combine("nested", "deeper", "c.bin"), 300);

        var result = await new DiskUsageScanner().ScanAsync(_root, progress: null, CancellationToken.None);

        Assert.Equal(600, result.Root.SizeBytes);
        Assert.Equal(3, result.FilesExamined);

        var nested = result.Root.Children.Single(child => child.Name == "nested");
        Assert.Equal(500, nested.SizeBytes);
    }

    [Fact]
    public async Task ScanAsync_SortsChildrenBiggestFirst()
    {
        WriteFile(Path.Combine("small", "a.bin"), 100);
        WriteFile(Path.Combine("large", "b.bin"), 900);

        var result = await new DiskUsageScanner().ScanAsync(_root, progress: null, CancellationToken.None);

        Assert.Equal("large", result.Root.Children[0].Name);
    }

    [Fact]
    public async Task ScanAsync_RollsSmallFilesIntoOneNode()
    {
        // A drive holds hundreds of thousands of files and a treemap can show a few hundred;
        // keeping them all would cost memory to draw rectangles too small to see.
        for (int index = 0; index < DiskUsageScanner.MaxFileNodesPerFolder + 10; index++)
        {
            WriteFile($"file-{index:00}.bin", index + 1);
        }

        var result = await new DiskUsageScanner().ScanAsync(_root, progress: null, CancellationToken.None);

        Assert.Equal(DiskUsageScanner.MaxFileNodesPerFolder + 1, result.Root.Children.Count);
        Assert.Contains(result.Root.Children, child => child.Name.StartsWith(DiskUsageScanner.AggregateNodeKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScanAsync_AggregateNodeCarriesTheSizeOfWhatItReplaced()
    {
        int total = 0;
        for (int index = 0; index < DiskUsageScanner.MaxFileNodesPerFolder + 5; index++)
        {
            WriteFile($"file-{index:00}.bin", index + 1);
            total += index + 1;
        }

        var result = await new DiskUsageScanner().ScanAsync(_root, progress: null, CancellationToken.None);

        Assert.Equal(total, result.Root.SizeBytes);
        Assert.Equal(total, result.Root.Children.Sum(child => child.SizeBytes));
    }

    [Fact]
    public async Task ScanAsync_EmptyFolder_IsAZeroSizedNodeNotAFailure()
    {
        var result = await new DiskUsageScanner().ScanAsync(_root, progress: null, CancellationToken.None);

        Assert.Equal(0, result.Root.SizeBytes);
        Assert.Empty(result.Root.Children);
    }

    [Fact]
    public async Task ScanAsync_MissingFolder_ReportsItRatherThanThrowing()
    {
        var result = await new DiskUsageScanner().ScanAsync(Path.Combine(_root, "never-existed"), progress: null, CancellationToken.None);

        Assert.Equal(0, result.Root.SizeBytes);
        Assert.True(result.UnreadableFolders > 0);
    }

    [Fact]
    public async Task ScanAsync_EmptyPath_IsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(() => new DiskUsageScanner().ScanAsync("   ", progress: null, CancellationToken.None));

    [Fact]
    public async Task ScanAsync_FoldersWithNothingInThem_AreLeftOutOfTheTree()
    {
        // An empty folder on a treemap is a rectangle with no area — noise in the tree.
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        WriteFile(Path.Combine("has-content", "a.bin"), 100);

        var result = await new DiskUsageScanner().ScanAsync(_root, progress: null, CancellationToken.None);

        Assert.Single(result.Root.Children);
        Assert.Equal("has-content", result.Root.Children[0].Name);
    }

    [Fact]
    public async Task ScanAsync_Cancellation_StopsTheScan()
    {
        WriteFile("a.bin", 100);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DiskUsageScanner().ScanAsync(_root, progress: null, cancelled.Token));
    }
}
