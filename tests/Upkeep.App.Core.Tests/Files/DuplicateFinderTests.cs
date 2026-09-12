using System.Text;
using Upkeep.App.Core.Files;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Files;

public class DuplicateFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"upkeep-dupes-{Guid.NewGuid():N}");
    private readonly FakeFileIdentityReader _identities = new();

    public DuplicateFinderTests() => Directory.CreateDirectory(_root);

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

    private DuplicateFinder CreateFinder() => new(_identities);

    private FileScanScope Scope(long minimumBytes = 1) => new() { Roots = [_root], MinimumFileBytes = minimumBytes };

    private string WriteFile(string relativePath, string content, DateTime? lastWriteUtc = null)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);

        if (lastWriteUtc is not null)
        {
            File.SetLastWriteTimeUtc(path, lastWriteUtc.Value);
        }

        return path;
    }

    [Fact]
    public async Task FindAsync_IdenticalContent_IsGroupedTogether()
    {
        WriteFile("a.txt", "the same bytes");
        WriteFile(Path.Combine("nested", "b.txt"), "the same bytes");

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.CopyCount);
    }

    [Fact]
    public async Task FindAsync_SameNameDifferentContent_IsNotADuplicate()
    {
        // Matching by name is exactly the guess this product refuses to make.
        WriteFile("report.txt", "one version of the file");
        WriteFile(Path.Combine("nested", "report.txt"), "a different version!!!!");

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FindAsync_SameSizeDifferentContent_IsNotADuplicate()
    {
        WriteFile("a.bin", "aaaaaaaaaaaaaaaa");
        WriteFile("b.bin", "bbbbbbbbbbbbbbbb");

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FindAsync_FilesLargerThanThePartialHashWindow_AreStillCompared()
    {
        // Two files identical for the first 64 KB and different after it must not be reported as
        // copies — the partial hash is an optimization, not the answer.
        string shared = new('x', 70 * 1024);
        WriteFile("big-a.bin", shared + "ending one");
        WriteFile("big-b.bin", shared + "ending two");

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FindAsync_KeepsTheOldestCopy()
    {
        // The original is usually where it was filed deliberately; the newer ones are the accidents.
        string original = WriteFile("original.txt", "keep me", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteFile(Path.Combine("Downloads", "copy.txt"), "keep me", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        var group = Assert.Single(result.Groups);
        Assert.Equal(original, group.Keep.Path);
        Assert.Single(group.Extras);
    }

    [Fact]
    public async Task FindAsync_HardLinks_AreNotOfferedAsReclaimableSpace()
    {
        // Byte-identical by definition, but deleting one frees nothing at all.
        string first = WriteFile("linked-a.txt", "same bytes on disk");
        string second = WriteFile("linked-b.txt", "same bytes on disk");
        _identities.LinkTogether(first, second);

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        Assert.Empty(result.Groups);
        Assert.Equal(0, result.ReclaimableBytes);
    }

    [Fact]
    public async Task FindAsync_ThreeCopiesWhereTwoAreHardLinked_ReportsOnlyTheRealExtra()
    {
        string first = WriteFile("one.txt", "three of these");
        string second = WriteFile("two.txt", "three of these");
        string third = WriteFile("three.txt", "three of these");
        _identities.LinkTogether(first, second);

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.CopyCount);
        Assert.Equal(group.SizeBytes, group.ReclaimableBytes);
        Assert.Contains(third, group.Files.Select(file => file.Path));
    }

    [Fact]
    public async Task FindAsync_ReclaimableBytes_CountsEveryCopyButOne()
    {
        WriteFile("a.txt", "0123456789");
        WriteFile("b.txt", "0123456789");
        WriteFile("c.txt", "0123456789");

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        var group = Assert.Single(result.Groups);
        Assert.Equal(group.SizeBytes * 2, result.ReclaimableBytes);
    }

    [Fact]
    public async Task FindAsync_FilesBelowTheSizeFloor_AreIgnored()
    {
        WriteFile("tiny-a.txt", "hi");
        WriteFile("tiny-b.txt", "hi");

        var result = await CreateFinder().FindAsync(Scope(minimumBytes: 1024), progress: null, CancellationToken.None);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FindAsync_GroupsAreOrderedByWhatTheyWouldGiveBack()
    {
        WriteFile("small-a.txt", new string('s', 200));
        WriteFile("small-b.txt", new string('s', 200));
        WriteFile("big-a.txt", new string('b', 5000));
        WriteFile("big-b.txt", new string('b', 5000));

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        Assert.Equal(2, result.Groups.Count);
        Assert.True(result.Groups[0].ReclaimableBytes > result.Groups[1].ReclaimableBytes);
    }

    [Fact]
    public async Task FindAsync_NothingToFind_IsAnEmptyResultNotAFailure()
    {
        WriteFile("only-one.txt", "unique content");

        var result = await CreateFinder().FindAsync(Scope(), progress: null, CancellationToken.None);

        Assert.Empty(result.Groups);
        Assert.Equal(1, result.FilesExamined);
    }

    [Fact]
    public async Task FindAsync_Cancellation_StopsTheScan()
    {
        WriteFile("a.txt", "content");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateFinder().FindAsync(Scope(), progress: null, cancelled.Token));
    }
}
