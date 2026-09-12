using Upkeep.App.Core.Sessions;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class HistoryViewModelTests : IDisposable
{
    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-history-vm-{Guid.NewGuid():N}");
    private readonly SessionJournal _journal;
    private readonly FakeSessionReverter _reverter;

    public HistoryViewModelTests()
    {
        _journal = new SessionJournal(_journalDirectory);
        _reverter = new FakeSessionReverter(_journal);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _journal.Dispose();

        try
        {
            Directory.Delete(_journalDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private HistoryViewModel CreateViewModel() =>
        new(_journal, _reverter, new FakeLocalizationService(), new FakeAppLogger());

    private async Task<SessionManifest> WriteSessionAsync(
        SessionKind kind = SessionKind.Cleanup,
        DateTimeOffset? startedAt = null,
        bool completed = true,
        string? restorePoint = null,
        bool restorePointReused = false,
        params SessionEntry[] entries)
    {
        var started = startedAt ?? DateTimeOffset.UtcNow;
        var session = new SessionManifest
        {
            Id = SessionManifest.NewId(started),
            Kind = kind,
            StartedAt = started,
            CompletedAt = completed ? started.AddMinutes(1) : null,
            RestorePointDescription = restorePoint,
            RestorePointWasReused = restorePointReused,
            Entries = entries,
        };

        return await _journal.SaveAsync(session, CancellationToken.None);
    }

    private static FileQuarantinedEntry Quarantined(long sizeBytes = 2048) =>
        new(@"C:\Users\a\leftover.txt", @"C:\Quarantine\leftover.txt", sizeBytes) { Completed = true };

    private static FileDeletedEntry Deleted(long sizeBytes = 4096) =>
        new(@"C:\Windows\Temp\cache.tmp", sizeBytes) { Completed = true };

    [Fact]
    public async Task LoadAsync_NothingHasBeenDoneYet_SaysSo()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Empty(viewModel.Sessions);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_ShowsNewestFirst()
    {
        await WriteSessionAsync(startedAt: DateTimeOffset.UtcNow.AddHours(-2), entries: Deleted());
        await WriteSessionAsync(kind: SessionKind.Apps, startedAt: DateTimeOffset.UtcNow, entries: Deleted());

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal("HistoryKindApps", viewModel.Sessions[0].KindLabel);
        Assert.Equal("HistoryKindCleanup", viewModel.Sessions[1].KindLabel);
    }

    [Fact]
    public async Task LoadAsync_SessionThatFreedSpace_SaysHowMuch()
    {
        await WriteSessionAsync(entries: Deleted(4096));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.Sessions[0].HasFreedDisplay);
        Assert.Contains("HistoryFreedFormat", viewModel.Sessions[0].FreedDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_SessionThatFreedNothing_ShowsNoFreedLine()
    {
        // A quarantined file hasn't reclaimed the space yet, so "0 B freed" would be wrong.
        await WriteSessionAsync(entries: Quarantined());

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Sessions[0].HasFreedDisplay);
        Assert.Null(viewModel.Sessions[0].FreedDisplay);
    }

    [Fact]
    public async Task LoadAsync_InterruptedSession_IsShownAsUnfinishedRatherThanHidden()
    {
        await WriteSessionAsync(completed: false, entries: Quarantined());

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.Sessions[0].HasUnfinishedNote);
        Assert.Equal("HistoryUnfinished", viewModel.Sessions[0].UnfinishedNote);
    }

    [Fact]
    public async Task LoadAsync_RestorePoint_SaysWhetherItWasMadeOrReused()
    {
        // The user is told which, rather than being told a new one exists (ADR-0006).
        await WriteSessionAsync(restorePoint: "Upkeep cleanup", restorePointReused: true, entries: Quarantined());

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("HistoryRestorePointReused", viewModel.Sessions[0].RestorePointNote);
    }

    [Fact]
    public async Task LoadAsync_NoRestorePoint_ShowsNoNote()
    {
        await WriteSessionAsync(entries: Quarantined());

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Sessions[0].HasRestorePointNote);
    }

    [Fact]
    public async Task LoadAsync_SessionWithNothingReversible_IsListedButCannotBePutBack()
    {
        // History is the record of what was done, not only of what can be undone.
        await WriteSessionAsync(entries: Deleted());

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(Assert.Single(viewModel.Sessions).CanRevert);
    }

    [Fact]
    public async Task RevertAsync_PutsTheSessionBackAndReportsWhatHappened()
    {
        await WriteSessionAsync(entries: Quarantined());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.RevertAsync(viewModel.Sessions[0], CancellationToken.None);

        Assert.Single(_reverter.RevertedSessionIds);
        Assert.Contains("HistoryRevertResultFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevertAsync_PartlyReverted_ReportsBothWhatWorkedAndWhatDidNot()
    {
        // Replacing one message with the other would hide half of what happened.
        await WriteSessionAsync(entries: Quarantined());
        _reverter.Outcome = new RevertOutcome(RevertedCount: 2, FailedCount: 1, SkippedCount: 0);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.RevertAsync(viewModel.Sessions[0], CancellationToken.None);

        Assert.Contains("HistoryRevertResultFormat", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("HistoryRevertFailedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevertAsync_NothingCouldBePutBack_SaysSo()
    {
        await WriteSessionAsync(entries: Quarantined());
        _reverter.Outcome = new RevertOutcome(RevertedCount: 0, FailedCount: 0, SkippedCount: 1);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.RevertAsync(viewModel.Sessions[0], CancellationToken.None);

        Assert.Equal("HistoryNothingToRevert", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RevertAsync_SessionWithNothingReversible_IsNotSentToTheReverter()
    {
        await WriteSessionAsync(entries: Deleted());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.RevertAsync(viewModel.Sessions[0], CancellationToken.None);

        Assert.Empty(_reverter.RevertedSessionIds);
        Assert.Equal("HistoryNothingToRevert", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RevertAsync_AlreadyRevertedSession_IsNotOfferedAgain()
    {
        await WriteSessionAsync(entries: Quarantined());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.RevertAsync(viewModel.Sessions[0], CancellationToken.None);

        // The list is rebuilt from the journal, which now carries RevertedAt.
        var session = Assert.Single(viewModel.Sessions);
        Assert.False(session.CanRevert);
        Assert.True(session.HasRevertedNote);
    }
}
