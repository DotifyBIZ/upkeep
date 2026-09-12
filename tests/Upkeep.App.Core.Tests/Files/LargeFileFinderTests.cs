using Upkeep.App.Core.Files;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Files;

public class LargeFileFinderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"upkeep-large-{Guid.NewGuid():N}");

    public LargeFileFinderTests() => Directory.CreateDirectory(_root);

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

    private static LargeFileFinder CreateFinder() => new(new FixedTimeProvider(Now));

    private FileScanScope Scope() => new() { Roots = [_root], MinimumFileBytes = 1 };

    private string WriteFile(string name, int sizeBytes, TimeSpan age)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(path, (Now - age).UtcDateTime);
        return path;
    }

    [Fact]
    public async Task FindAsync_BigFile_QualifiesOnSize()
    {
        WriteFile("big.bin", 5000, TimeSpan.FromDays(1));
        var criteria = new LargeFileCriteria { MinimumBytes = 1000, MinimumAge = TimeSpan.FromDays(365) };

        var results = await CreateFinder().FindAsync(Scope(), criteria, progress: null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsLarge);
        Assert.False(result.IsOld);
    }

    [Fact]
    public async Task FindAsync_ForgottenFile_QualifiesOnAge()
    {
        WriteFile("old.bin", 50, TimeSpan.FromDays(400));
        var criteria = new LargeFileCriteria { MinimumBytes = 1_000_000, MinimumAge = TimeSpan.FromDays(180) };

        var results = await CreateFinder().FindAsync(Scope(), criteria, progress: null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.True(result.IsOld);
        Assert.False(result.IsLarge);
    }

    [Fact]
    public async Task FindAsync_RequireBoth_OnlyKeepsFilesThatAreBigAndForgotten()
    {
        WriteFile("big-and-old.bin", 5000, TimeSpan.FromDays(400));
        WriteFile("big-but-recent.bin", 5000, TimeSpan.FromDays(1));
        WriteFile("old-but-small.bin", 10, TimeSpan.FromDays(400));
        var criteria = new LargeFileCriteria { MinimumBytes = 1000, MinimumAge = TimeSpan.FromDays(180), RequireBoth = true };

        var results = await CreateFinder().FindAsync(Scope(), criteria, progress: null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.EndsWith("big-and-old.bin", result.File.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAsync_OrdersByTheBiggestWinFirst()
    {
        WriteFile("medium.bin", 3000, TimeSpan.FromDays(1));
        WriteFile("largest.bin", 9000, TimeSpan.FromDays(1));
        WriteFile("smallest.bin", 1500, TimeSpan.FromDays(1));
        var criteria = new LargeFileCriteria { MinimumBytes = 1000, MinimumAge = TimeSpan.FromDays(9999) };

        var results = await CreateFinder().FindAsync(Scope(), criteria, progress: null, CancellationToken.None);

        Assert.Equal([9000, 3000, 1500], results.Select(result => result.SizeBytes));
    }

    [Fact]
    public async Task FindAsync_NothingQualifies_IsEmpty()
    {
        WriteFile("ordinary.bin", 100, TimeSpan.FromDays(2));
        var criteria = new LargeFileCriteria { MinimumBytes = 1_000_000, MinimumAge = TimeSpan.FromDays(365) };

        Assert.Empty(await CreateFinder().FindAsync(Scope(), criteria, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_DefaultCriteria_AreTheOnesTheUiShows()
    {
        var criteria = new LargeFileCriteria();

        Assert.Equal(250L * 1024 * 1024, criteria.MinimumBytes);
        Assert.Equal(TimeSpan.FromDays(180), criteria.MinimumAge);
        Assert.False(criteria.RequireBoth);

        await Task.CompletedTask;
    }
}
