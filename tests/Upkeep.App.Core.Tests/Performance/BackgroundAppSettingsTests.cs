using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Performance;

public class BackgroundAppSettingsTests
{
    private const string Music = "Microsoft.ZuneMusic_8wekyb3d8bbwe";
    private const string Xbox = "Microsoft.GamingApp_8wekyb3d8bbwe";
    private const string Plumbing = "Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy";

    private readonly FakeRegistryProbe _registry = new();
    private readonly FakeCatalog _catalog = new();

    private sealed class FakeCatalog : IStorePackageCatalog
    {
        public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> UserFacingPackages() => Names;
    }

    private BackgroundAppSettings CreateSettings() => new(_registry, _catalog);

    private static string Key(string familyName) => BackgroundAccessValues.KeyFor(familyName);

    [Theory]
    [InlineData(null, null, null, BackgroundAccess.PowerOptimized)]
    [InlineData(1, 1, null, BackgroundAccess.Never)]
    [InlineData(null, 1, null, BackgroundAccess.Never)]
    [InlineData(0, 0, 1, BackgroundAccess.Always)]
    [InlineData(0, 0, 0, BackgroundAccess.PowerOptimized)]
    public void Classify_MatchesWhatSettingsShows(int? disabled, int? byUser, int? ignoreSaver, BackgroundAccess expected)
    {
        Assert.Equal(expected, BackgroundAccessValues.Classify(disabled, byUser, ignoreSaver));
    }

    [Theory]
    [InlineData(Music, true)]
    [InlineData("Valve.Steam_abcdefghjkmnp", true)]
    [InlineData(@"..\Run", false)]
    [InlineData("NoPublisher", false)]
    [InlineData("Name_TOOSHORT", false)]
    [InlineData(null, false)]
    public void IsValidFamilyName_RefusesAnythingThatIsNotOne(string? name, bool valid)
    {
        Assert.Equal(valid, BackgroundAccessValues.IsValidFamilyName(name));
    }

    [Fact]
    public void Read_ListsOnlyAppsThePersonWouldRecogniseByName()
    {
        _registry.AddKey(RegistryHiveName.CurrentUser, Key(Music));
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Xbox), "Disabled", 1);
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Xbox), "DisabledByUser", 1);
        _registry.AddKey(RegistryHiveName.CurrentUser, Key(Plumbing));
        _catalog.Names[Music] = "Media Player";
        _catalog.Names[Xbox] = "Xbox";

        var apps = CreateSettings().Read();

        Assert.Equal(
            [new BackgroundApp(Music, "Media Player", BackgroundAccess.PowerOptimized), new BackgroundApp(Xbox, "Xbox", BackgroundAccess.Never)],
            apps);
    }

    [Fact]
    public void SetAllowed_Off_WritesWhatSettingsWritesForNever()
    {
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "IgnoreBatterySaver", 1);
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "NCBEnabled", 1);

        Assert.True(CreateSettings().SetAllowed(Music, allowed: false, out _));

        var values = _registry.GetValues(RegistryHiveName.CurrentUser, Key(Music)).ToDictionary(value => value.Name, value => value.Number);
        Assert.Equal(1, values["Disabled"]);
        Assert.Equal(1, values["DisabledByUser"]);
        Assert.False(values.ContainsKey("IgnoreBatterySaver"));

        // Values Upkeep doesn't manage are left alone.
        Assert.Equal(1, values["NCBEnabled"]);
    }

    [Fact]
    public void SetAllowed_On_HandsTheDecisionBackToWindows()
    {
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "Disabled", 1);
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "DisabledByUser", 1);

        Assert.True(CreateSettings().SetAllowed(Music, allowed: true, out _));

        Assert.Empty(_registry.GetValues(RegistryHiveName.CurrentUser, Key(Music)));
    }

    [Fact]
    public void SetAllowed_Unwritable_ReportsTheFailure()
    {
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "Disabled", 0);
        _registry.Unwritable.Add(Key(Music));

        Assert.False(CreateSettings().SetAllowed(Music, allowed: false, out string? failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void SetAllowed_InvalidName_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSettings().SetAllowed(@"..\Run", allowed: false, out _));
    }

    [Fact]
    public void SnapshotThenRestore_PutsAlwaysBackExactly()
    {
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "Disabled", 0);
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "DisabledByUser", 0);
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "IgnoreBatterySaver", 1);
        _registry.AddValue(RegistryHiveName.CurrentUser, Key(Music), "NCBEnabled", 1);
        var settings = CreateSettings();

        var snapshot = settings.Snapshot(Music);
        settings.SetAllowed(Music, allowed: false, out _);

        Assert.Equal(3, snapshot.Count);
        Assert.True(settings.Restore(Music, snapshot, out _));
        _catalog.Names[Music] = "Media Player";
        Assert.Equal(BackgroundAccess.Always, Assert.Single(settings.Read()).Access);
    }

    [Fact]
    public void Restore_ValueItDoesNotManage_IsRefused()
    {
        Assert.False(CreateSettings().Restore(Music, [new("Run", "String", Text: "evil.exe")], out string? failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void Restore_WriteFails_ReportsIt()
    {
        _registry.Unwritable.Add(Key(Music));

        Assert.False(CreateSettings().Restore(Music, [new("Disabled", "DWord", Number: 1)], out _));
    }
}
