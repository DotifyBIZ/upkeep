using Upkeep.App.Core.Files;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Files;

public class FileActionExecutorTests : IDisposable
{
    private readonly FakeWellKnownPaths _paths = new();
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-fileactions-{Guid.NewGuid():N}");
    private readonly SessionJournal _journal;
    private readonly FileAppLogger _logger;
    private readonly FileActionExecutor _executor;

    public FileActionExecutorTests()
    {
        Directory.CreateDirectory(_workingDirectory);
        _journal = new SessionJournal(Path.Combine(_workingDirectory, "sessions"));
        _logger = new FileAppLogger(Path.Combine(_workingDirectory, "logs"));
        _executor = new FileActionExecutor(new QuarantineStore(_paths), _journal, _logger);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _journal.Dispose();
        _logger.Dispose();
        _paths.Dispose();

        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string CreateFile(string name, int sizeBytes = 100) =>
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.UserProfile, name), sizeBytes);

    [Fact]
    public async Task RemoveAsync_ByDefault_MovesFilesToQuarantineRatherThanDeletingThem()
    {
        string file = CreateFile("holiday.mp4", 500);

        var outcome = await _executor.RemoveAsync(new FileActionRequest([file]), CancellationToken.None);

        Assert.True(outcome.WasQuarantined);
        Assert.Equal(1, outcome.RemovedCount);
        Assert.False(File.Exists(file));

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var entry = Assert.IsType<FileQuarantinedEntry>(Assert.Single(session!.Entries));
        Assert.True(entry.Completed);
        Assert.True(entry.IsReversible);
        Assert.True(File.Exists(entry.QuarantinePath));
    }

    [Fact]
    public async Task RemoveAsync_Quarantined_ReportsNoSpaceFreedYet()
    {
        // The bytes come back when the quarantine is purged, not now. Saying otherwise would be
        // the "freed" number that isn't (ADR-0006).
        string file = CreateFile("big.iso", 4096);

        var outcome = await _executor.RemoveAsync(new FileActionRequest([file]), CancellationToken.None);

        Assert.Equal(4096, outcome.AffectedBytes);
        Assert.Equal(0, outcome.FreedBytes);
    }

    [Fact]
    public async Task RemoveAsync_PermanentDelete_RemovesTheFileAndCountsTheSpace()
    {
        string file = CreateFile("scratch.tmp", 256);

        var outcome = await _executor.RemoveAsync(new FileActionRequest([file], DeletePermanently: true), CancellationToken.None);

        Assert.False(outcome.WasQuarantined);
        Assert.Equal(256, outcome.FreedBytes);
        Assert.False(File.Exists(file));

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var entry = Assert.IsType<FileDeletedEntry>(Assert.Single(session!.Entries));
        Assert.False(entry.IsReversible);
    }

    [Fact]
    public async Task RemoveAsync_JournalsEveryFileBeforeTouchingIt()
    {
        string first = CreateFile("a.bin");
        string second = CreateFile("b.bin");

        var outcome = await _executor.RemoveAsync(new FileActionRequest([first, second]), CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.Equal(2, session!.Entries.Count);
        Assert.All(session.Entries, entry => Assert.True(entry.Completed));
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task RemoveAsync_OneFileAlreadyGone_DoesNotStopTheRest()
    {
        string missing = Path.Combine(_paths.UserProfile, "vanished.bin");
        string present = CreateFile("present.bin", 300);

        var outcome = await _executor.RemoveAsync(new FileActionRequest([missing, present]), CancellationToken.None);

        Assert.Equal(1, outcome.RemovedCount);
        Assert.Equal(1, outcome.FailedCount);
        Assert.False(File.Exists(present));
    }

    [Fact]
    public async Task RemoveAsync_QuarantinedFile_CanBePutBackFromTheJournal()
    {
        // The whole point of quarantine: the session record is enough to undo it.
        string file = CreateFile("notes.txt", 64);
        var outcome = await _executor.RemoveAsync(new FileActionRequest([file]), CancellationToken.None);
        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var entry = (FileQuarantinedEntry)session!.Entries[0];

        bool restored = await new QuarantineStore(_paths).RestoreAsync(entry.QuarantinePath, entry.OriginalPath, CancellationToken.None);

        Assert.True(restored);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task RemoveAsync_NothingSelected_IsAnEmptySessionNotAFailure()
    {
        var outcome = await _executor.RemoveAsync(new FileActionRequest([]), CancellationToken.None);

        Assert.Equal(0, outcome.RemovedCount);
        Assert.Equal(0, outcome.FailedCount);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.Empty(session!.Entries);
    }

    [Fact]
    public async Task RemoveAsync_RecordsTheSessionAsAFilesSession()
    {
        string file = CreateFile("a.bin");

        var outcome = await _executor.RemoveAsync(new FileActionRequest([file]), CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.Equal(SessionKind.Files, session!.Kind);
        Assert.True(session.CanRevert);
    }

    [Fact]
    public async Task RemoveAsync_PermanentDelete_LeavesNothingToRevert()
    {
        string file = CreateFile("gone.bin");

        var outcome = await _executor.RemoveAsync(new FileActionRequest([file], DeletePermanently: true), CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.False(session!.CanRevert);
    }

    [Fact]
    public async Task RemoveAsync_Cancellation_StopsBetweenFiles()
    {
        string file = CreateFile("a.bin");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _executor.RemoveAsync(new FileActionRequest([file]), cancelled.Token));
    }
}
