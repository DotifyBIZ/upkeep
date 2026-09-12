using Upkeep.App.Core.Settings;

namespace Upkeep.App.Core.Tests.Settings;

public class FileAppSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"upkeep-settings-{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public FileAppSettingsServiceTests() => Directory.CreateDirectory(_directory);

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
    public async Task LoadAsync_NoFileYet_ReturnsDefaults()
    {
        var service = new FileAppSettingsService(SettingsPath);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.True(settings.CheckForUpdatesEnabled);
        Assert.Equal(string.Empty, settings.LanguageOverride);
        Assert.Equal(7, settings.QuarantineRetentionDays);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsEverySetting()
    {
        var service = new FileAppSettingsService(SettingsPath);
        await service.SaveAsync(
            new AppSettings { CheckForUpdatesEnabled = false, LanguageOverride = "pl-PL", QuarantineRetentionDays = 14 },
            CancellationToken.None);

        var loaded = await service.LoadAsync(CancellationToken.None);

        Assert.False(loaded.CheckForUpdatesEnabled);
        Assert.Equal("pl-PL", loaded.LanguageOverride);
        Assert.Equal(14, loaded.QuarantineRetentionDays);
    }

    [Fact]
    public async Task SaveAsync_DirectoryDoesNotExist_CreatesIt()
    {
        string nested = Path.Combine(_directory, "nested", "settings.json");
        var service = new FileAppSettingsService(nested);

        await service.SaveAsync(new AppSettings(), CancellationToken.None);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_FallsBackToDefaultsInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(SettingsPath, "{ this is not json", CancellationToken.None);
        var service = new FileAppSettingsService(SettingsPath);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.True(settings.CheckForUpdatesEnabled);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9999, 90)]
    [InlineData(30, 30)]
    public async Task LoadAsync_ClampsQuarantineRetention(int stored, int expected)
    {
        // This file sits in a user-writable directory and decides when real files are destroyed,
        // so it is treated as untrusted input rather than trusted state.
        await File.WriteAllTextAsync(SettingsPath, $$"""{"QuarantineRetentionDays": {{stored}} }""", CancellationToken.None);
        var service = new FileAppSettingsService(SettingsPath);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, settings.QuarantineRetentionDays);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedLanguageOverride_FallsBackToFollowingWindows()
    {
        await File.WriteAllTextAsync(SettingsPath, """{"LanguageOverride": "de-DE"}""", CancellationToken.None);
        var service = new FileAppSettingsService(SettingsPath);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(string.Empty, settings.LanguageOverride);
    }

    [Fact]
    public async Task LoadForStartup_ReadsTheSameFileAsLoadAsync()
    {
        var service = new FileAppSettingsService(SettingsPath);
        await service.SaveAsync(new AppSettings { LanguageOverride = "pl-PL" }, CancellationToken.None);

        var startupSettings = FileAppSettingsService.LoadForStartup(SettingsPath);

        Assert.Equal("pl-PL", startupSettings.LanguageOverride);
    }

    [Fact]
    public void LoadForStartup_MissingFile_ReturnsDefaults()
    {
        var settings = FileAppSettingsService.LoadForStartup(Path.Combine(_directory, "absent.json"));

        Assert.True(settings.CheckForUpdatesEnabled);
    }
}
