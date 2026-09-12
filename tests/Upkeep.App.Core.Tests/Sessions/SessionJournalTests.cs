using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Sessions;

public class SessionJournalTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 18, 30, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"upkeep-sessions-{Guid.NewGuid():N}");

    private SessionJournal CreateJournal() => new(_directory, new FixedTimeProvider(Now));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task StartAsync_WritesTheManifestBeforeAnyWorkHappens()
    {
        var journal = CreateJournal();

        var session = await journal.StartAsync(SessionKind.Cleanup, CancellationToken.None);

        // A manifest that only exists after a successful run is no use to the run that didn't
        // finish — the file has to be on disk from the start.
        Assert.True(File.Exists(Path.Combine(_directory, $"{session.Id}.json")));
        Assert.Equal(SessionKind.Cleanup, session.Kind);
        Assert.Equal(Now, session.StartedAt);
    }

    [Fact]
    public async Task AppendAsync_PersistsTheEntryImmediately()
    {
        var journal = CreateJournal();
        var session = await journal.StartAsync(SessionKind.Cleanup, CancellationToken.None);

        session = await journal.AppendAsync(session, new FileDeletedEntry(@"C:\Temp\a.tmp", 100), CancellationToken.None);

        var reloaded = await journal.LoadAsync(session.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal(@"C:\Temp\a.tmp", Assert.IsType<FileDeletedEntry>(entry).Path);
        // Written before it ran, so it is not completed yet.
        Assert.False(entry.Completed);
    }

    [Fact]
    public async Task UpdateEntryAsync_MarksAnEntryCompleted()
    {
        var journal = CreateJournal();
        var session = await journal.StartAsync(SessionKind.Cleanup, CancellationToken.None);
        session = await journal.AppendAsync(session, new FileDeletedEntry(@"C:\Temp\a.tmp", 100), CancellationToken.None);

        session = await journal.UpdateEntryAsync(
            session,
            0,
            ((FileDeletedEntry)session.Entries[0]) with { Completed = true },
            CancellationToken.None);

        var reloaded = await journal.LoadAsync(session.Id, CancellationToken.None);
        Assert.True(reloaded!.Entries[0].Completed);
        Assert.Equal(100, reloaded.FreedBytes);
    }

    [Fact]
    public async Task UpdateEntryAsync_IndexOutsideTheSession_IsRejected()
    {
        var journal = CreateJournal();
        var session = await journal.StartAsync(SessionKind.Cleanup, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => journal.UpdateEntryAsync(session, 0, new FileDeletedEntry("x", 1), CancellationToken.None));
    }

    [Fact]
    public async Task RoundTrip_KeepsEveryEntryType()
    {
        // The discriminators are persisted data; a type that doesn't survive the round trip means
        // a past session can no longer be reverted.
        var journal = CreateJournal();
        var session = await journal.StartAsync(SessionKind.Startup, CancellationToken.None);

        SessionEntry[] entries =
        [
            new FileQuarantinedEntry(@"C:\Users\a\big.iso", @"C:\Quarantine\big.iso", 2048),
            new FileDeletedEntry(@"C:\Temp\a.tmp", 100),
            new RegistryValueChangedEntry("HKCU", @"Software\X", "Run", "String", "old"),
            new RegistryKeyRemovedEntry("HKLM", @"SOFTWARE\X", @"C:\backup.json"),
            new ServiceStartTypeChangedEntry("DiagTrack", "Automatic", "Disabled"),
            new StartupItemToggledEntry("Discord", "RunKey", true),
            new ScheduledTaskToggledEntry(@"\GoogleUpdate", true),
            new SystemSettingChangedEntry("AnimationEffects", "1", "0"),
            new IrreversibleOperationEntry("ComponentStoreCleanup", "Windows Update cleanup", 1024),
        ];

        foreach (var entry in entries)
        {
            session = await journal.AppendAsync(session, entry, CancellationToken.None);
        }

        var reloaded = await journal.LoadAsync(session.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(entries.Length, reloaded.Entries.Count);
        Assert.Collection(
            reloaded.Entries,
            entry => Assert.IsType<FileQuarantinedEntry>(entry),
            entry => Assert.IsType<FileDeletedEntry>(entry),
            entry => Assert.IsType<RegistryValueChangedEntry>(entry),
            entry => Assert.IsType<RegistryKeyRemovedEntry>(entry),
            entry => Assert.IsType<ServiceStartTypeChangedEntry>(entry),
            entry => Assert.IsType<StartupItemToggledEntry>(entry),
            entry => Assert.IsType<ScheduledTaskToggledEntry>(entry),
            entry => Assert.IsType<SystemSettingChangedEntry>(entry),
            entry => Assert.IsType<IrreversibleOperationEntry>(entry));
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        var journal = CreateJournal();
        var older = await journal.SaveAsync(
            new SessionManifest { Id = "20260101-000000-aaaaaaaa", Kind = SessionKind.Cleanup, StartedAt = Now.AddDays(-2) },
            CancellationToken.None);
        var newer = await journal.SaveAsync(
            new SessionManifest { Id = "20260901-000000-bbbbbbbb", Kind = SessionKind.Apps, StartedAt = Now },
            CancellationToken.None);

        var sessions = await journal.ListAsync(CancellationToken.None);

        Assert.Equal([newer.Id, older.Id], sessions.Select(session => session.Id));
    }

    [Fact]
    public async Task ListAsync_SkipsAnUnreadableManifest()
    {
        // One corrupt file must not take down the whole History page.
        var journal = CreateJournal();
        await journal.StartAsync(SessionKind.Cleanup, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_directory, "broken.json"), "{ not json", CancellationToken.None);

        var sessions = await journal.ListAsync(CancellationToken.None);

        Assert.Single(sessions);
    }

    [Fact]
    public async Task ListAsync_NoJournalYet_IsEmpty()
    {
        var journal = new SessionJournal(Path.Combine(_directory, "never-used"));

        Assert.Empty(await journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_UnknownSession_ReturnsNull() =>
        Assert.Null(await CreateJournal().LoadAsync("20260101-000000-nosuchid", CancellationToken.None));

    [Theory]
    [InlineData(@"..\..\evil")]
    [InlineData(@"sub\dir")]
    [InlineData("/etc/passwd")]
    public async Task LoadAsync_SessionIdWithPathSeparators_IsRejected(string sessionId)
    {
        // Ids come back from the UI and from disk; a traversal here would read or write outside
        // the journal folder.
        var journal = CreateJournal();

        await Assert.ThrowsAsync<ArgumentException>(() => journal.LoadAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public void NewId_IsSortableAndUnique()
    {
        string first = SessionManifest.NewId(Now);
        string second = SessionManifest.NewId(Now);

        Assert.NotEqual(first, second);
        Assert.StartsWith("20260912-183000-", first, StringComparison.Ordinal);
    }
}
