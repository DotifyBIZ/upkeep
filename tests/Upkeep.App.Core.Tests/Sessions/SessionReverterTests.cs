using System.Text.Json;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;
using Upkeep.App.Core.Updates;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Sessions;

public class SessionReverterTests : IDisposable
{
    private const string KeyPath = @"Software\Vendor\App";

    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-revert-{Guid.NewGuid():N}");
    private readonly FakeWellKnownPaths _paths = new();
    private readonly FakeRegistryProbe _registry = new();
    private readonly FakeElevationService _elevation = new();
    private readonly FakeStartupItemScanner _startupScanner = new();
    private readonly FakeStartupItemToggler _startupToggler = new();
    private readonly FakePerformanceSettings _performance = new();
    private readonly SessionJournal _journal;
    private readonly FileAppLogger _logger;

    public SessionReverterTests()
    {
        Directory.CreateDirectory(_workingDirectory);
        _journal = new SessionJournal(Path.Combine(_workingDirectory, "sessions"));
        _logger = new FileAppLogger(Path.Combine(_workingDirectory, "logs"));
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);

    private SessionReverter CreateReverter() => new(
        new QuarantineStore(_paths),
        new RegistryKeyBackupService(_registry),
        _elevation,
        _startupScanner,
        _startupToggler,
        _performance,
        _journal,
        _logger,
        new FixedTimeProvider(Now));

    private static SessionManifest Session(params SessionEntry[] entries) => new()
    {
        Id = SessionManifest.NewId(DateTimeOffset.UtcNow),
        Kind = SessionKind.Startup,
        Entries = entries,
    };

    private static StartupItem StartupItem(string id = "Discord", bool isEnabled = true) => new()
    {
        Id = id,
        Name = id,
        Source = StartupSource.RunKeyCurrentUser,
        IsEnabled = isEnabled,
    };

    [Fact]
    public async Task RevertAsync_SessionWithNothingReversible_ChangesNothing()
    {
        var session = Session(new FileDeletedEntry(@"C:\Temp\cache.tmp", 1024) { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(0, outcome.RevertedCount);
        Assert.False(outcome.AnythingReverted);
        Assert.Equal(1, outcome.SkippedCount);
    }

    [Fact]
    public async Task RevertAsync_SessionAlreadyReverted_IsNotRevertedAgain()
    {
        var session = Session(new SystemSettingChangedEntry(PerformanceSettingIds.Animations, "True", "False") { Completed = true })
            with
        { RevertedAt = DateTimeOffset.UtcNow };

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Empty(_performance.Changes);
        Assert.Equal(0, outcome.RevertedCount);
    }

    [Fact]
    public async Task RevertAsync_EntryThatNeverCompleted_IsSkippedRatherThanAttempted()
    {
        // An interrupted session records what it was about to do; undoing something that never
        // happened would change the machine rather than restore it.
        var session = Session(new SystemSettingChangedEntry(PerformanceSettingIds.Animations, "True", "False"));

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Empty(_performance.Changes);
        Assert.Equal(1, outcome.SkippedCount);
    }

    [Fact]
    public async Task RevertAsync_QuarantinedFile_IsPutBackWhereItCameFrom()
    {
        string original = Path.Combine(_workingDirectory, "leftover.txt");
        await File.WriteAllTextAsync(original, "contents");

        var store = new QuarantineStore(_paths);
        var quarantined = await store.QuarantineAsync(original, "session-1", CancellationToken.None);
        Assert.True(quarantined.Success);
        Assert.False(File.Exists(original));

        var session = Session(new FileQuarantinedEntry(original, quarantined.QuarantinePath!, 8) { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.True(File.Exists(original));
        Assert.Equal("contents", await File.ReadAllTextAsync(original));
        Assert.Equal(1, outcome.RevertedCount);
    }

    [Fact]
    public async Task RevertAsync_RemovedRegistryKey_IsRestoredWithItsValues()
    {
        string backupPath = Path.Combine(_workingDirectory, "key.regbackup.json");
        var backup = new RegistryKeyBackup(
            RegistryHiveName.CurrentUser.ToString(),
            KeyPath,
            [new RegistryValueSnapshot("InstallPath", "String", Text: @"C:\Program Files\App")],
            [new RegistryKeyBackup(
                RegistryHiveName.CurrentUser.ToString(),
                $@"{KeyPath}\Settings",
                [new RegistryValueSnapshot("Theme", "String", Text: "dark")],
                [])]);
        await File.WriteAllTextAsync(backupPath, JsonSerializer.Serialize(backup));

        var session = Session(new RegistryKeyRemovedEntry(RegistryHiveName.CurrentUser.ToString(), KeyPath, backupPath) { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(1, outcome.RevertedCount);
        Assert.Equal(@"C:\Program Files\App", _registry.GetStringValue(RegistryHiveName.CurrentUser, KeyPath, "InstallPath"));
        Assert.Equal("dark", _registry.GetStringValue(RegistryHiveName.CurrentUser, $@"{KeyPath}\Settings", "Theme"));
    }

    [Fact]
    public async Task RevertAsync_RegistryBackupNoLongerOnDisk_CountsAsFailedNotReverted()
    {
        // Quarantine and backups age out; History must not claim it put something back when the
        // file it needed was gone.
        var session = Session(new RegistryKeyRemovedEntry(
            RegistryHiveName.CurrentUser.ToString(),
            KeyPath,
            Path.Combine(_workingDirectory, "missing.json"))
        { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(0, outcome.RevertedCount);
        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public async Task RevertAsync_ServiceStartType_GoesBackThroughTheElevatedHelper()
    {
        // A revert is not a reason to bypass the boundary: the helper classifies the service again.
        _elevation.EnqueueResponse(new ServiceChangeResponse(true, null, null));
        var session = Session(new ServiceStartTypeChangedEntry("DiagTrack", "Automatic", "Disabled") { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        var request = Assert.IsType<SetServiceStartTypeRequest>(Assert.Single(_elevation.SentRequests));
        Assert.Equal("DiagTrack", request.ServiceName);
        Assert.Equal(ServiceStartType.Automatic, request.StartType);
        Assert.Equal(1, outcome.RevertedCount);
    }

    [Fact]
    public async Task RevertAsync_ServiceRevertDeclinedAtTheElevationPrompt_Fails()
    {
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);
        var session = Session(new ServiceStartTypeChangedEntry("DiagTrack", "Automatic", "Disabled") { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Empty(_elevation.SentRequests);
        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public async Task RevertAsync_StartupItem_UsesThePathThatWritesNoJournalEntry()
    {
        // A revert undoes a session rather than adding to it.
        _startupScanner.Items.Add(StartupItem());
        var session = Session(new StartupItemToggledEntry("Discord", nameof(StartupSource.RunKeyCurrentUser), PreviouslyEnabled: true) { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(("Discord", true), Assert.Single(_startupToggler.DirectChanges));
        Assert.Empty(_startupToggler.JournaledChanges);
        Assert.Equal(1, outcome.RevertedCount);
    }

    [Fact]
    public async Task RevertAsync_StartupItemNoLongerOnTheMachine_Fails()
    {
        var session = Session(new StartupItemToggledEntry("Discord", nameof(StartupSource.RunKeyCurrentUser), PreviouslyEnabled: true) { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Empty(_startupToggler.DirectChanges);
        Assert.Equal(1, outcome.FailedCount);
    }

    [Theory]
    [InlineData(PerformanceSettingIds.Animations, "True", "animations=True")]
    [InlineData(PerformanceSettingIds.Transparency, "False", "transparency=False")]
    public async Task RevertAsync_VisualEffect_PutsThePreviousValueBack(string settingId, string previous, string expected)
    {
        var session = Session(new SystemSettingChangedEntry(settingId, previous, "irrelevant") { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(_performance.Changes));
        Assert.Equal(1, outcome.RevertedCount);
    }

    [Fact]
    public async Task RevertAsync_PowerPlan_PutsThePreviousPlanBack()
    {
        var previous = Guid.NewGuid();
        var session = Session(new SystemSettingChangedEntry(PerformanceSettingIds.PowerPlan, previous.ToString(), Guid.NewGuid().ToString()) { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal($"power-plan={previous}", Assert.Single(_performance.Changes));
        Assert.Equal(1, outcome.RevertedCount);
    }

    [Fact]
    public async Task RevertAsync_SettingIdThisBuildDoesNotKnow_IsRefusedRatherThanGuessedAt()
    {
        var session = Session(new SystemSettingChangedEntry("something-invented", "True", "False") { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Empty(_performance.Changes);
        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public async Task RevertAsync_UnparseableRecordedValue_Fails()
    {
        var session = Session(new SystemSettingChangedEntry(PerformanceSettingIds.Animations, "not a bool", "False") { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Empty(_performance.Changes);
        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public async Task RevertAsync_UndoesNewestFirst()
    {
        // A session unwinds in the reverse of the order it was wound.
        var session = Session(
            new SystemSettingChangedEntry(PerformanceSettingIds.Animations, "True", "False") { Completed = true },
            new SystemSettingChangedEntry(PerformanceSettingIds.Transparency, "True", "False") { Completed = true });

        await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(["transparency=True", "animations=True"], _performance.Changes);
    }

    [Fact]
    public async Task RevertAsync_MixedSession_CountsWhatHappenedHonestly()
    {
        _elevation.EnqueueResponse(new ServiceChangeResponse(true, null, null));

        var session = Session(
            new IrreversibleOperationEntry("recycle-bin", "Emptied the Recycle Bin", 2048) { Completed = true },
            new ServiceStartTypeChangedEntry("DiagTrack", "Automatic", "Disabled") { Completed = true },
            new StartupItemToggledEntry("Missing", nameof(StartupSource.RunKeyCurrentUser), PreviouslyEnabled: true) { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(1, outcome.RevertedCount);
        Assert.Equal(1, outcome.FailedCount);
        Assert.Equal(1, outcome.SkippedCount);
    }

    [Fact]
    public async Task RevertAsync_StampsRevertedAtSoHistoryDoesNotOfferItTwice()
    {
        var session = Session(new SystemSettingChangedEntry(PerformanceSettingIds.Animations, "True", "False") { Completed = true });

        (var reverted, _) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.NotNull(reverted.RevertedAt);
        Assert.False(reverted.CanRevert);
    }

    [Fact]
    public async Task RevertAsync_PartialFailure_StillStampsTheSession()
    {
        // Offering the whole session again would repeat the parts that already worked.
        var session = Session(new SystemSettingChangedEntry("something-invented", "True", "False") { Completed = true });

        (var reverted, _) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.NotNull(reverted.RevertedAt);
    }

    [Fact]
    public async Task RevertAsync_PauseThatStillHadTimeLeft_PausesAgainForTheDaysRemaining()
    {
        // The entry records when the old pause was due to end; what goes back is what it had left.
        var session = Session(new SystemSettingChangedEntry(
            UpdateSettingIds.Pause,
            WindowsUpdatePolicy.FormatTime(Now.AddDays(9)),
            WindowsUpdatePolicy.FormatTime(Now.AddDays(30)))
        { Completed = true });

        _elevation.EnqueueResponse(new WindowsUpdateStateResponse(true, WindowsUpdateState.NotConfigured, null));

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(1, outcome.RevertedCount);
        Assert.Equal(9, Assert.IsType<SetUpdatePauseRequest>(Assert.Single(_elevation.SentRequests)).Days);
    }

    [Fact]
    public async Task RevertAsync_UpdatesWereNotPausedBefore_ResumesRatherThanPausing()
    {
        var session = Session(new SystemSettingChangedEntry(
            UpdateSettingIds.Pause,
            string.Empty,
            WindowsUpdatePolicy.FormatTime(Now.AddDays(7)))
        { Completed = true });

        _elevation.EnqueueResponse(new WindowsUpdateStateResponse(true, WindowsUpdateState.NotConfigured, null));

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(1, outcome.RevertedCount);
        Assert.Equal(0, Assert.IsType<SetUpdatePauseRequest>(Assert.Single(_elevation.SentRequests)).Days);
    }

    [Fact]
    public async Task RevertAsync_PauseHadAlreadyLapsed_ResumesRatherThanStartingANewOne()
    {
        // A pause whose time passed while the session sat in History must not come back to life.
        var session = Session(new SystemSettingChangedEntry(
            UpdateSettingIds.Pause,
            WindowsUpdatePolicy.FormatTime(Now.AddDays(-2)),
            string.Empty)
        { Completed = true });

        _elevation.EnqueueResponse(new WindowsUpdateStateResponse(true, WindowsUpdateState.NotConfigured, null));

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(1, outcome.RevertedCount);
        Assert.Equal(0, Assert.IsType<SetUpdatePauseRequest>(Assert.Single(_elevation.SentRequests)).Days);
    }

    [Fact]
    public async Task RevertAsync_Deferral_PutsBothPeriodsBack()
    {
        var session = Session(new SystemSettingChangedEntry(
            UpdateSettingIds.Deferral,
            WindowsUpdatePolicy.FormatDeferral(180, 7),
            WindowsUpdatePolicy.FormatDeferral(0, 0))
        { Completed = true });

        _elevation.EnqueueResponse(new WindowsUpdateStateResponse(true, WindowsUpdateState.NotConfigured, null));

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(1, outcome.RevertedCount);

        var request = Assert.IsType<SetUpdateDeferralRequest>(Assert.Single(_elevation.SentRequests));
        Assert.Equal(180, request.FeatureDays);
        Assert.Equal(7, request.QualityDays);
    }

    [Fact]
    public async Task RevertAsync_DeferralRecordedInAShapeThisBuildCannotRead_IsRefusedNotGuessed()
    {
        var session = Session(new SystemSettingChangedEntry(UpdateSettingIds.Deferral, "180", "0/0") { Completed = true });

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(0, outcome.RevertedCount);
        Assert.Equal(1, outcome.FailedCount);
        Assert.Empty(_elevation.SentRequests);
    }

    [Fact]
    public async Task RevertAsync_HelperRefusesTheScheduleChange_CountsAsFailedNotReverted()
    {
        var session = Session(new SystemSettingChangedEntry(UpdateSettingIds.Deferral, "180/7", "0/0") { Completed = true });

        _elevation.EnqueueResponse(new WindowsUpdateStateResponse(false, WindowsUpdateState.NotConfigured, "refused"));

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(0, outcome.RevertedCount);
        Assert.Equal(1, outcome.FailedCount);
    }

    [Fact]
    public async Task RevertAsync_ElevationDeclined_LeavesTheScheduleAlone()
    {
        // Without the helper there is no way to write these keys, and no way to pretend otherwise.
        var session = Session(new SystemSettingChangedEntry(UpdateSettingIds.Pause, string.Empty, "irrelevant") { Completed = true });
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);

        (_, var outcome) = await CreateReverter().RevertAsync(session, CancellationToken.None);

        Assert.Equal(1, outcome.FailedCount);
        Assert.Empty(_elevation.SentRequests);
    }
}
