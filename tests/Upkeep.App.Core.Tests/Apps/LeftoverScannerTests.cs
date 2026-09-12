using Upkeep.App.Core.Apps;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Apps;

public class LeftoverScannerTests : IDisposable
{
    private readonly FakeWellKnownPaths _paths = new();
    private readonly FakeRegistryProbe _registry = new();

    private LeftoverScanner CreateScanner() => new(_paths, _registry);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _paths.Dispose();
    }

    private static InstalledApp App(string name = "Zoom Workplace", string? publisher = "Zoom Communications", string? installLocation = null) => new()
    {
        Id = "Zoom",
        DisplayName = name,
        Publisher = publisher,
        InstallLocation = installLocation,
        Source = AppSource.Desktop,
        Scope = AppScope.CurrentUser,
    };

    [Theory]
    [InlineData("Zoom Workplace", "zoomworkplace")]
    [InlineData("7-Zip 24.09 (x64)", "7zip")]
    [InlineData("Adobe Acrobat (64-bit)", "adobeacrobat")]
    [InlineData("Blender 4.5", "blender")]
    public void Normalize_StripsVersionsAndNoise(string displayName, string expected) =>
        Assert.Equal(expected, LeftoverScanner.Normalize(displayName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NothingToNormalize_IsNull(string? value) => Assert.Null(LeftoverScanner.Normalize(value));

    [Fact]
    public void MatchesApp_ExactAndContainedNames_Match()
    {
        Assert.True(LeftoverScanner.MatchesApp("Zoom", "zoomworkplace", null));
        Assert.True(LeftoverScanner.MatchesApp("Zoom Workplace", "zoomworkplace", null));
    }

    [Fact]
    public void MatchesApp_SimilarButDifferentApp_DoesNotMatch()
    {
        // "Zoho" is not "Zoom", and a leftover scan that can't tell them apart is a data-loss bug.
        Assert.False(LeftoverScanner.MatchesApp("Zoho", "zoomworkplace", null));
        Assert.False(LeftoverScanner.MatchesApp("Discord", "zoomworkplace", null));
    }

    [Fact]
    public void MatchesApp_PublisherFolder_Matches() =>
        Assert.True(LeftoverScanner.MatchesApp("Zoom Communications", "zoomworkplace", "zoomcommunications"));

    [Fact]
    public void MatchesApp_VeryShortFolderName_NeverMatches()
    {
        // Two-character names match far too much to be safe.
        Assert.False(LeftoverScanner.MatchesApp("Go", "go", null));
    }

    [Fact]
    public void IsRemovableFolder_AppFolderUnderAppData_IsAllowed() =>
        Assert.True(CreateScanner().IsRemovableFolder(Path.Combine(_paths.LocalAppData, "Zoom")));

    [Fact]
    public void IsRemovableFolder_TheRootItself_IsRefused()
    {
        // A badly-named app must never be able to nominate %LocalAppData% itself.
        Assert.False(CreateScanner().IsRemovableFolder(_paths.LocalAppData));
    }

    [Fact]
    public void IsRemovableFolder_SharedVendorFolder_IsRefused()
    {
        Assert.False(CreateScanner().IsRemovableFolder(Path.Combine(_paths.LocalAppData, "Microsoft")));
        Assert.False(CreateScanner().IsRemovableFolder(Path.Combine(_paths.LocalAppData, "Microsoft", "Edge")));
    }

    [Fact]
    public void IsRemovableFolder_OutsideTheKnownRoots_IsRefused()
    {
        Assert.False(CreateScanner().IsRemovableFolder(Path.Combine(_paths.WindowsDirectory, "System32")));
        Assert.False(CreateScanner().IsRemovableFolder(Path.Combine(_paths.UserProfile, "Documents", "Zoom")));
    }

    [Fact]
    public void IsRemovableFolder_BuriedTooDeep_IsRefused()
    {
        // Anything four levels into AppData belongs to something else's structure.
        string deep = Path.Combine(_paths.LocalAppData, "a", "b", "c", "d");

        Assert.False(CreateScanner().IsRemovableFolder(deep));
    }

    [Theory]
    [InlineData(@"Software\Zoom", true)]
    [InlineData(@"Software\Zoom Communications\Zoom", true)]
    [InlineData(@"Software", false)]
    [InlineData(@"Software\Microsoft", false)]
    [InlineData(@"Software\Microsoft\Windows", false)]
    [InlineData(@"Software\Classes", false)]
    [InlineData(@"Software\Policies\Microsoft", false)]
    [InlineData(@"Software\A\B\C", false)]
    [InlineData(@"SYSTEM\CurrentControlSet", false)]
    public void IsRemovableKey_OnlyAppScopedKeysAreAllowed(string keyPath, bool expected) =>
        Assert.Equal(expected, LeftoverScanner.IsRemovableKey(keyPath));

    [Fact]
    public void Scan_FindsTheAppsOwnFolders()
    {
        string roaming = _paths.CreateUnder(Path.Combine("AppData", "Zoom"));
        FakeWellKnownPaths.WriteFile(Path.Combine(roaming, "settings.json"), 184);

        var items = CreateScanner().Scan(App(), CancellationToken.None);

        var folder = Assert.Single(items, item => item.Kind == LeftoverKind.Folder);
        Assert.Equal(roaming, folder.Path);
        Assert.Equal(184, folder.SizeBytes);
    }

    [Fact]
    public void Scan_FindsFoldersUnderThePublisher()
    {
        string nested = _paths.CreateUnder(Path.Combine("LocalAppData", "Zoom Communications", "Zoom Workplace"));
        FakeWellKnownPaths.WriteFile(Path.Combine(nested, "cache.bin"), 22);

        var items = CreateScanner().Scan(App(), CancellationToken.None);

        Assert.Contains(items, item => item.Path == nested);
    }

    [Fact]
    public void Scan_NeverOffersThePublishersOwnFolder()
    {
        // Other products from the same vendor live under it — the same reason the publisher's
        // registry key is never offered either.
        string publisherFolder = _paths.CreateUnder(Path.Combine("LocalAppData", "Zoom Communications"));
        string appFolder = Path.Combine(publisherFolder, "Zoom Workplace");
        Directory.CreateDirectory(appFolder);
        FakeWellKnownPaths.WriteFile(Path.Combine(appFolder, "cache.bin"), 10);

        var items = CreateScanner().Scan(App(), CancellationToken.None);

        Assert.DoesNotContain(items, item => item.Path == publisherFolder);
        Assert.Contains(items, item => item.Path == appFolder);
    }

    [Fact]
    public void Scan_IgnoresFoldersBelongingToOtherApps()
    {
        _paths.CreateUnder(Path.Combine("LocalAppData", "Discord"));
        _paths.CreateUnder(Path.Combine("LocalAppData", "Zoho"));

        var items = CreateScanner().Scan(App(), CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public void Scan_MachineWideFolder_IsMarkedAsNeedingAdministratorRights()
    {
        string programData = _paths.CreateUnder(Path.Combine("ProgramData", "Zoom"));
        FakeWellKnownPaths.WriteFile(Path.Combine(programData, "machine.cfg"), 4);

        var items = CreateScanner().Scan(App(), CancellationToken.None);

        var item = Assert.Single(items, candidate => candidate.Path == programData);
        Assert.True(item.RequiresElevation);
    }

    [Fact]
    public void Scan_FindsTheAppsRegistryKeys()
    {
        _registry.AddKey(RegistryHiveName.CurrentUser, @"Software\Zoom Workplace");
        _registry.AddKey(RegistryHiveName.LocalMachine, @"SOFTWARE\Zoom Communications\Zoom Workplace");

        var items = CreateScanner().Scan(App(), CancellationToken.None);

        var keys = items.Where(item => item.Kind == LeftoverKind.RegistryKey).ToList();
        Assert.Equal(2, keys.Count);
        Assert.Contains(keys, key => key.Hive == RegistryHiveName.LocalMachine && key.RequiresElevation);
    }

    [Fact]
    public void Scan_NeverOffersThePublishersOwnKey()
    {
        // Other products from the same vendor live under it.
        _registry.AddKey(RegistryHiveName.CurrentUser, @"Software\Zoom Communications");

        var items = CreateScanner().Scan(App(), CancellationToken.None);

        Assert.DoesNotContain(items, item => item.Path.Equals(@"Software\Zoom Communications", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_IncludesTheStatedInstallLocation()
    {
        string installLocation = _paths.CreateUnder(Path.Combine("LocalAppData", "Programs", "Zoom"));
        FakeWellKnownPaths.WriteFile(Path.Combine(installLocation, "Zoom.exe"), 500);

        var items = CreateScanner().Scan(App(installLocation: installLocation), CancellationToken.None);

        Assert.Contains(items, item => item.Path == installLocation && item.SizeBytes == 500);
    }

    [Fact]
    public void Scan_InstallLocationOutsideTheAllowedRoots_IsRefused()
    {
        // An app claiming C:\Windows as its install location doesn't get to have it removed.
        string windows = _paths.CreateUnder(Path.Combine("Windows", "System32"));

        var items = CreateScanner().Scan(App(installLocation: windows), CancellationToken.None);

        Assert.DoesNotContain(items, item => item.Path == windows);
    }

    [Fact]
    public void Scan_AppWithNoUsableName_FindsNothing() =>
        Assert.Empty(CreateScanner().Scan(App(name: "   ", publisher: null), CancellationToken.None));

    [Fact]
    public void Scan_NothingLeftBehind_IsAnEmptyList() =>
        Assert.Empty(CreateScanner().Scan(App(), CancellationToken.None));
}
