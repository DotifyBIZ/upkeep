using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Platform;

public class RegistryKeyBackupServiceTests
{
    private const string KeyPath = @"Software\Vendor\App";

    private readonly FakeRegistryProbe _registry = new();

    private RegistryKeyBackupService CreateService() => new(_registry);

    [Fact]
    public void Capture_KeyThatIsNotThere_ReturnsNothing()
    {
        Assert.Null(CreateService().Capture(RegistryHiveName.CurrentUser, KeyPath));
    }

    [Fact]
    public void Capture_RecordsTheValuesAndNotJustTheNames()
    {
        // A backup holding only subkey names cannot put anything back, which would make the
        // journal's "reversible" claim a lie.
        _registry.AddValue(RegistryHiveName.CurrentUser, KeyPath, "InstallPath", @"C:\Program Files\App");
        _registry.AddValue(RegistryHiveName.CurrentUser, KeyPath, "Build", 42);

        var backup = CreateService().Capture(RegistryHiveName.CurrentUser, KeyPath);

        Assert.NotNull(backup);
        Assert.Equal(KeyPath, backup.KeyPath);
        Assert.Equal(@"C:\Program Files\App", backup.Values.Single(value => value.Name == "InstallPath").Text);
        Assert.Equal(42, backup.Values.Single(value => value.Name == "Build").Number);
    }

    [Fact]
    public void Capture_WalksIntoSubKeys()
    {
        _registry.AddValue(RegistryHiveName.CurrentUser, KeyPath, "Top", "top value");
        _registry.AddValue(RegistryHiveName.CurrentUser, $@"{KeyPath}\Settings", "Theme", "dark");

        var backup = CreateService().Capture(RegistryHiveName.CurrentUser, KeyPath);

        var child = Assert.Single(backup!.SubKeys);
        Assert.Equal($@"{KeyPath}\Settings", child.KeyPath);
        Assert.Equal("dark", child.Values.Single().Text);
    }

    [Fact]
    public void CaptureThenRestore_PutsEveryValueBack()
    {
        _registry.AddValue(RegistryHiveName.CurrentUser, KeyPath, "InstallPath", @"C:\Program Files\App");
        _registry.AddValue(RegistryHiveName.CurrentUser, KeyPath, "Flags", new byte[] { 1, 2, 3 });
        _registry.AddValue(RegistryHiveName.CurrentUser, $@"{KeyPath}\Settings", "Theme", "dark");

        var service = CreateService();
        var backup = service.Capture(RegistryHiveName.CurrentUser, KeyPath);

        Assert.True(_registry.DeleteCurrentUserKeyTree(KeyPath, out _));
        Assert.True(service.Restore(backup!, out string? failure));
        Assert.Null(failure);

        Assert.Equal(@"C:\Program Files\App", _registry.GetStringValue(RegistryHiveName.CurrentUser, KeyPath, "InstallPath"));
        Assert.Equal([1, 2, 3], _registry.GetBinaryValue(RegistryHiveName.CurrentUser, KeyPath, "Flags"));
        Assert.Equal("dark", _registry.GetStringValue(RegistryHiveName.CurrentUser, $@"{KeyPath}\Settings", "Theme"));
    }

    [Fact]
    public void Restore_MachineWideKey_IsRefused()
    {
        // Machine-wide writes are the elevated helper's business; restoring one from the shell
        // would be a hole in ADR-0005.
        var backup = new RegistryKeyBackup(RegistryHiveName.LocalMachine.ToString(), KeyPath, [], []);

        Assert.False(CreateService().Restore(backup, out string? failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void Restore_RegistryRefusesTheWrite_ReportsItRatherThanClaimingSuccess()
    {
        _registry.AddValue(RegistryHiveName.CurrentUser, KeyPath, "InstallPath", "somewhere");
        var service = CreateService();
        var backup = service.Capture(RegistryHiveName.CurrentUser, KeyPath);

        _registry.Unwritable.Add(KeyPath);

        Assert.False(service.Restore(backup!, out string? failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void Capture_DeeplyNestedKey_StopsAtTheDepthLimit()
    {
        // An unbounded walk is the one way a backup could become the expensive part of an uninstall.
        _registry.AddKey(RegistryHiveName.CurrentUser, KeyPath);
        string path = KeyPath;
        for (int depth = 0; depth < RegistryKeyBackupService.MaxDepth + 5; depth++)
        {
            path = $@"{path}\Level{depth}";
            _registry.AddValue(RegistryHiveName.CurrentUser, path, "Value", depth);
        }

        var backup = CreateService().Capture(RegistryHiveName.CurrentUser, KeyPath);

        int levels = 0;
        for (var current = backup; current is not null; current = current.SubKeys.Count > 0 ? current.SubKeys[0] : null)
        {
            levels++;
        }

        Assert.Equal(RegistryKeyBackupService.MaxDepth + 1, levels);
    }
}
