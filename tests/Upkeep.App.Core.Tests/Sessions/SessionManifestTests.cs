using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Tests.Sessions;

public class SessionManifestTests
{
    private static SessionManifest WithEntries(params SessionEntry[] entries) =>
        new() { Id = "20260912-120000-abcdef12", Kind = SessionKind.Cleanup, Entries = entries };

    [Fact]
    public void FreedBytes_CountsOnlyCompletedEntries()
    {
        var manifest = WithEntries(
            new FileDeletedEntry("a", 100) { Completed = true },
            new FileDeletedEntry("b", 500));

        Assert.Equal(100, manifest.FreedBytes);
    }

    [Fact]
    public void FreedBytes_DoesNotCountQuarantinedFiles()
    {
        // Moving a file to quarantine frees nothing until the quarantine is purged; reporting it
        // as freed would be the "7.7 GB freed" that isn't (ADR-0006).
        var manifest = WithEntries(new FileQuarantinedEntry("a", "q", 900) { Completed = true });

        Assert.Equal(0, manifest.FreedBytes);
        Assert.Equal(900, manifest.QuarantinedBytes);
    }

    [Fact]
    public void CanRevert_RequiresACompletedReversibleEntry()
    {
        Assert.False(WithEntries(new FileDeletedEntry("a", 1) { Completed = true }).CanRevert);
        Assert.False(WithEntries(new ServiceStartTypeChangedEntry("X", "Auto", "Disabled")).CanRevert);
        Assert.True(WithEntries(new ServiceStartTypeChangedEntry("X", "Auto", "Disabled") { Completed = true }).CanRevert);
    }

    [Fact]
    public void CanRevert_IsFalseOnceTheSessionHasBeenReverted()
    {
        var manifest = WithEntries(new ServiceStartTypeChangedEntry("X", "Auto", "Disabled") { Completed = true })
            with
        { RevertedAt = DateTimeOffset.UtcNow };

        Assert.False(manifest.CanRevert);
    }

    [Fact]
    public void FailedCount_CountsEntriesThatRecordedAFailure()
    {
        var manifest = WithEntries(
            new FileDeletedEntry("a", 1) { Completed = true },
            new FileDeletedEntry("b", 1) { FailureDetail = "in use" });

        Assert.Equal(1, manifest.CompletedCount);
        Assert.Equal(1, manifest.FailedCount);
    }

    [Fact]
    public void IrreversibleOperations_AreNeverReversible()
    {
        Assert.False(new IrreversibleOperationEntry("RecycleBinEmptied", "Recycle Bin").IsReversible);
        Assert.False(new FileDeletedEntry("a", 1).IsReversible);
    }

    [Fact]
    public void ReversibleEntryTypes_AllReportThemselvesAsSuch()
    {
        SessionEntry[] reversible =
        [
            new FileQuarantinedEntry("a", "q", 1),
            new RegistryValueChangedEntry("HKCU", "Software\\X", "V", "String", "old"),
            new RegistryKeyRemovedEntry("HKLM", "SOFTWARE\\X", "backup"),
            new ServiceStartTypeChangedEntry("X", "Auto", "Disabled"),
            new StartupItemToggledEntry("X", "RunKey", true),
            new ScheduledTaskToggledEntry("\\Task", true),
            new SystemSettingChangedEntry("Setting", "1", "0"),
        ];

        Assert.All(reversible, entry => Assert.True(entry.IsReversible));
    }
}
