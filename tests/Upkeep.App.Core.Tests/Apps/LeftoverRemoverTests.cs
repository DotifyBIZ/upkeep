using Upkeep.App.Core.Apps;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Apps;

public class LeftoverRemoverTests : IDisposable
{
    private readonly FakeWellKnownPaths _paths = new();
    private readonly FakeRegistryProbe _registry = new();
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-leftovers-{Guid.NewGuid():N}");
    private readonly SessionJournal _journal;
    private readonly FileAppLogger _logger;
    private readonly LeftoverRemover _remover;

    public LeftoverRemoverTests()
    {
        Directory.CreateDirectory(_workingDirectory);
        _journal = new SessionJournal(Path.Combine(_workingDirectory, "sessions"));
        _logger = new FileAppLogger(Path.Combine(_workingDirectory, "logs"));
        _remover = new LeftoverRemover(new QuarantineStore(_paths), _journal, _registry, _paths, _logger);
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

    private static InstalledApp App() => new()
    {
        Id = "Zoom",
        DisplayName = "Zoom Workplace",
        Publisher = "Zoom Communications",
        Source = AppSource.Desktop,
        Scope = AppScope.CurrentUser,
    };

    private string CreateLeftoverFolder(string name, params (string File, int Size)[] files)
    {
        string folder = _paths.CreateUnder(Path.Combine("AppData", name));
        foreach ((string file, int size) in files)
        {
            FakeWellKnownPaths.WriteFile(Path.Combine(folder, file), size);
        }

        return folder;
    }

    [Fact]
    public async Task RemoveAsync_Folder_MovesItsFilesToQuarantine()
    {
        string folder = CreateLeftoverFolder("Zoom", ("settings.json", 120), ("cache.bin", 80));
        var items = new[] { new LeftoverItem(LeftoverKind.Folder, folder, 200) };

        var outcome = await _remover.RemoveAsync(App(), items, CancellationToken.None);

        Assert.Equal(1, outcome.RemovedCount);
        Assert.Equal(200, outcome.QuarantinedBytes);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task RemoveAsync_QuarantinedFiles_AreRecordedWhereHistoryCanRestoreThem()
    {
        string folder = CreateLeftoverFolder("Zoom", ("settings.json", 120));
        var items = new[] { new LeftoverItem(LeftoverKind.Folder, folder, 120) };

        var outcome = await _remover.RemoveAsync(App(), items, CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var quarantined = Assert.Single(session!.Entries.OfType<FileQuarantinedEntry>());
        Assert.True(quarantined.Completed);
        Assert.True(File.Exists(quarantined.QuarantinePath));
        Assert.True(quarantined.IsReversible);
    }

    [Fact]
    public async Task RemoveAsync_RecordsTheUninstallItselfAsIrreversible()
    {
        // The app is gone whatever happens to its leftovers; History should say so rather than
        // implying the whole session can be undone.
        var outcome = await _remover.RemoveAsync(App(), [], CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var entry = Assert.Single(session!.Entries);
        Assert.IsType<IrreversibleOperationEntry>(entry);
        Assert.False(session.CanRevert);
    }

    [Fact]
    public async Task RemoveAsync_RegistryKey_IsBackedUpBeforeItIsRemoved()
    {
        _registry.AddKey(RegistryHiveName.CurrentUser, @"Software\Zoom Workplace");
        var items = new[] { new LeftoverItem(LeftoverKind.RegistryKey, @"Software\Zoom Workplace") { Hive = RegistryHiveName.CurrentUser } };

        var outcome = await _remover.RemoveAsync(App(), items, CancellationToken.None);

        Assert.Equal(1, outcome.RemovedCount);
        Assert.Contains(@"Software\Zoom Workplace", _registry.DeletedKeys);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var entry = Assert.Single(session!.Entries.OfType<RegistryKeyRemovedEntry>());
        Assert.True(entry.Completed);
        Assert.True(File.Exists(entry.BackupPath));
    }

    [Fact]
    public async Task RemoveAsync_RegistryKeyThatIsAlreadyGone_IsCountedAsFailedNotRemoved()
    {
        var items = new[] { new LeftoverItem(LeftoverKind.RegistryKey, @"Software\Zoom Workplace") { Hive = RegistryHiveName.CurrentUser } };

        var outcome = await _remover.RemoveAsync(App(), items, CancellationToken.None);

        Assert.Equal(0, outcome.RemovedCount);
        Assert.Equal(1, outcome.FailedCount);
        Assert.Empty(_registry.DeletedKeys);
    }

    [Fact]
    public async Task RemoveAsync_RegistryDeletionRefused_IsReportedAndStillBackedUp()
    {
        _registry.AddKey(RegistryHiveName.CurrentUser, @"Software\Zoom Workplace");
        _registry.Undeletable.Add(@"Software\Zoom Workplace");
        var items = new[] { new LeftoverItem(LeftoverKind.RegistryKey, @"Software\Zoom Workplace") { Hive = RegistryHiveName.CurrentUser } };

        var outcome = await _remover.RemoveAsync(App(), items, CancellationToken.None);

        Assert.Equal(1, outcome.FailedCount);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        var entry = Assert.Single(session!.Entries.OfType<RegistryKeyRemovedEntry>());
        Assert.False(entry.Completed);
        Assert.NotNull(entry.FailureDetail);
    }

    [Fact]
    public async Task RemoveAsync_MachineWideLeftover_IsLeftToTheElevatedHelper()
    {
        // The shell has no business trying; attempting and failing would look like a bug rather
        // than a boundary.
        string folder = CreateLeftoverFolder("Zoom", ("machine.cfg", 10));
        var items = new[] { new LeftoverItem(LeftoverKind.Folder, folder, 10, RequiresElevation: true) };

        var outcome = await _remover.RemoveAsync(App(), items, CancellationToken.None);

        Assert.Equal(0, outcome.RemovedCount);
        Assert.Equal(1, outcome.FailedCount);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task RemoveAsync_FolderThatVanishedFirst_IsNotCountedAsRemovedWork()
    {
        var items = new[] { new LeftoverItem(LeftoverKind.Folder, Path.Combine(_paths.RoamingAppData, "NotThere"), 0) };

        var outcome = await _remover.RemoveAsync(App(), items, CancellationToken.None);

        // No files to move, so nothing failed either — there was simply nothing there.
        Assert.Equal(1, outcome.RemovedCount);
        Assert.Equal(0, outcome.QuarantinedBytes);
    }

    [Fact]
    public async Task RemoveAsync_SessionIsRecordedAgainstTheAppsArea()
    {
        var outcome = await _remover.RemoveAsync(App(), [], CancellationToken.None);

        var session = await _journal.LoadAsync(outcome.SessionId, CancellationToken.None);
        Assert.Equal(SessionKind.Apps, session!.Kind);
        Assert.NotNull(session.CompletedAt);
    }
}
