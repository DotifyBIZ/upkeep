using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Quarantine;

public class QuarantineStoreTests : IDisposable
{
    private const string SessionId = "20260912-120000-abcdef12";

    private readonly FakeWellKnownPaths _paths = new();
    private readonly QuarantineStore _store;

    public QuarantineStoreTests() => _store = new QuarantineStore(_paths);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _paths.Dispose();
    }

    private string CreateUserFile(string name, int sizeBytes = 128)
    {
        string path = Path.Combine(_paths.UserProfile, name);
        FakeWellKnownPaths.WriteFile(path, sizeBytes);
        return path;
    }

    [Fact]
    public async Task QuarantineAsync_MovesTheFileOutOfTheUsersWay()
    {
        string original = CreateUserFile("holiday.mp4", 512);

        var result = await _store.QuarantineAsync(original, SessionId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(File.Exists(original));
        Assert.True(File.Exists(result.QuarantinePath));
        Assert.Contains(SessionId, result.QuarantinePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuarantineAsync_KeepsTheOriginalNameSoTheUserRecognizesIt()
    {
        string original = CreateUserFile("invoice-2026.pdf");

        var result = await _store.QuarantineAsync(original, SessionId, CancellationToken.None);

        Assert.EndsWith("invoice-2026.pdf", result.QuarantinePath!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuarantineAsync_TwoFilesWithTheSameName_BothSurvive()
    {
        string first = CreateUserFile("report.docx", 10);
        string secondFolder = _paths.CreateUnder(Path.Combine("Users", "tester", "archive"));
        string second = FakeWellKnownPaths.WriteFile(Path.Combine(secondFolder, "report.docx"), 20);

        var firstResult = await _store.QuarantineAsync(first, SessionId, CancellationToken.None);
        var secondResult = await _store.QuarantineAsync(second, SessionId, CancellationToken.None);

        Assert.True(firstResult.Success);
        Assert.True(secondResult.Success);
        Assert.NotEqual(firstResult.QuarantinePath, secondResult.QuarantinePath);
        Assert.True(File.Exists(firstResult.QuarantinePath));
        Assert.True(File.Exists(secondResult.QuarantinePath));
    }

    [Fact]
    public async Task QuarantineAsync_FileAlreadyGone_ReportsFailureInsteadOfThrowing()
    {
        var result = await _store.QuarantineAsync(Path.Combine(_paths.UserProfile, "vanished.txt"), SessionId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureDetail);
    }

    [Fact]
    public async Task RestoreAsync_PutsTheFileBackWhereItCameFrom()
    {
        string original = CreateUserFile("notes.txt", 64);
        var quarantined = await _store.QuarantineAsync(original, SessionId, CancellationToken.None);

        bool restored = await _store.RestoreAsync(quarantined.QuarantinePath!, original, CancellationToken.None);

        Assert.True(restored);
        Assert.True(File.Exists(original));
        Assert.Equal(64, new FileInfo(original).Length);
    }

    [Fact]
    public async Task RestoreAsync_SomethingElseTookTheName_RestoresAlongsideRatherThanOverwriting()
    {
        string original = CreateUserFile("config.json", 10);
        var quarantined = await _store.QuarantineAsync(original, SessionId, CancellationToken.None);
        // The app was reinstalled while the old copy sat in quarantine.
        FakeWellKnownPaths.WriteFile(original, 999);

        bool restored = await _store.RestoreAsync(quarantined.QuarantinePath!, original, CancellationToken.None);

        Assert.True(restored);
        Assert.Equal(999, new FileInfo(original).Length);
        Assert.True(File.Exists(Path.Combine(_paths.UserProfile, "config (restored 1).json")));
    }

    [Fact]
    public async Task RestoreAsync_QuarantinedFileIsGone_ReportsFailure() =>
        Assert.False(await _store.RestoreAsync(Path.Combine(_store.PrimaryRoot, "missing"), Path.Combine(_paths.UserProfile, "x.txt"), CancellationToken.None));

    [Fact]
    public async Task GetTotalBytes_SumsWhatIsHeld()
    {
        await _store.QuarantineAsync(CreateUserFile("a.bin", 100), SessionId, CancellationToken.None);
        await _store.QuarantineAsync(CreateUserFile("b.bin", 250), SessionId, CancellationToken.None);

        Assert.Equal(350, _store.GetTotalBytes());
    }

    [Fact]
    public async Task Purge_RemovesSessionsPastRetentionAndKeepsTheRest()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.QuarantineAsync(CreateUserFile("old.bin", 400), "20260101-000000-oldoldold", CancellationToken.None);
        await _store.QuarantineAsync(CreateUserFile("recent.bin", 150), SessionId, CancellationToken.None);

        string oldFolder = Path.Combine(_store.PrimaryRoot, "20260101-000000-oldoldold");
        Directory.SetCreationTimeUtc(oldFolder, now.UtcDateTime.AddDays(-30));

        long freed = _store.Purge(TimeSpan.FromDays(7), now);

        Assert.Equal(400, freed);
        Assert.False(Directory.Exists(oldFolder));
        Assert.True(Directory.Exists(Path.Combine(_store.PrimaryRoot, SessionId)));
    }

    [Fact]
    public async Task PurgeAll_EmptiesEverythingNow()
    {
        await _store.QuarantineAsync(CreateUserFile("a.bin", 100), SessionId, CancellationToken.None);

        long freed = _store.PurgeAll();

        Assert.Equal(100, freed);
        Assert.Equal(0, _store.GetTotalBytes());
    }

    [Fact]
    public void GetTotalBytes_NothingQuarantinedYet_IsZero() => Assert.Equal(0, _store.GetTotalBytes());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QuarantineAsync_EmptyPath_IsRejected(string path) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _store.QuarantineAsync(path, SessionId, CancellationToken.None));
}
