using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Settings;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly FakeAppSettingsService _settings = new();
    private readonly FakeUpdateCheckService _updates = new();
    private readonly FakeQuarantineStore _quarantine = new();
    private readonly FakeWindowsUiLauncher _launcher = new();

    private SettingsViewModel CreateViewModel(string? productVersion = "1.2.3") =>
        new(_settings, _updates, _quarantine, _launcher, new FakeLocalizationService(), new FakeAppLogger(), productVersion);

    private async Task<SettingsViewModel> LoadedAsync(string? productVersion = "1.2.3")
    {
        var viewModel = CreateViewModel(productVersion);
        await viewModel.LoadAsync(CancellationToken.None);
        return viewModel;
    }

    [Fact]
    public async Task LoadAsync_ShowsWhatIsSaved()
    {
        _settings.Settings = new AppSettings
        {
            CheckForUpdatesEnabled = false,
            LanguageOverride = "pl-PL",
            QuarantineRetentionDays = 30,
        };

        var viewModel = await LoadedAsync();

        Assert.False(viewModel.CheckForUpdatesEnabled);
        Assert.Equal("pl-PL", viewModel.SelectedLanguage?.Tag);
        Assert.Equal(30, viewModel.RetentionDays);
    }

    [Fact]
    public async Task LoadAsync_NoLanguageOverride_FollowsWindows()
    {
        var viewModel = await LoadedAsync();

        Assert.Equal(string.Empty, viewModel.SelectedLanguage?.Tag);
    }

    [Fact]
    public async Task LoadAsync_OffersOnlyTheLanguagesTheSettingsFileAccepts()
    {
        // Anything else is discarded by FileAppSettingsService on the next load, so offering more
        // would mean a choice that silently reverts.
        var viewModel = await LoadedAsync();

        Assert.Equal(["", "en-US", "pl-PL"], viewModel.Languages.Select(choice => choice.Tag));
    }

    [Fact]
    public async Task LoadAsync_RetentionOutsideWhatIsAccepted_IsBroughtIntoRange()
    {
        _settings.Settings = new AppSettings { QuarantineRetentionDays = 500 };

        var viewModel = await LoadedAsync();

        Assert.Equal(SettingsViewModel.MaximumRetentionDays, viewModel.RetentionDays);
    }

    [Fact]
    public async Task LoadAsync_ReadingTheSavedValues_IsNotTreatedAsAChange()
    {
        await LoadedAsync();

        Assert.Empty(_settings.Saves);
    }

    [Fact]
    public async Task LoadAsync_ShowsWhatQuarantineIsHolding()
    {
        _quarantine.TotalBytes = 4096;

        var viewModel = await LoadedAsync();

        Assert.Contains("SettingsQuarantineHoldingFormat", viewModel.QuarantineHolding, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_PersistsEveryControl()
    {
        var viewModel = await LoadedAsync();

        viewModel.SelectedLanguage = viewModel.Languages.Single(choice => choice.Tag == "pl-PL");
        viewModel.CheckForUpdatesEnabled = false;
        viewModel.RetentionDays = 14;
        await viewModel.SaveAsync(CancellationToken.None);

        var saved = Assert.Single(_settings.Saves);
        Assert.Equal("pl-PL", saved.LanguageOverride);
        Assert.False(saved.CheckForUpdatesEnabled);
        Assert.Equal(14, saved.QuarantineRetentionDays);
    }

    [Fact]
    public async Task SaveAsync_RetentionBeyondWhatIsAccepted_IsClampedBeforeItIsWritten()
    {
        var viewModel = await LoadedAsync();

        viewModel.RetentionDays = 5000;
        await viewModel.SaveAsync(CancellationToken.None);

        Assert.Equal(SettingsViewModel.MaximumRetentionDays, Assert.Single(_settings.Saves).QuarantineRetentionDays);
    }

    [Fact]
    public async Task SaveAsync_SettingsFileNotWritable_SaysSoRatherThanFailing()
    {
        var viewModel = await LoadedAsync();
        _settings.ThrowOnSave = true;

        viewModel.RetentionDays = 20;
        await viewModel.SaveAsync(CancellationToken.None);

        Assert.True(viewModel.HasStatusMessage);
    }

    [Fact]
    public async Task CheckForUpdatesNowAsync_NewerVersion_SaysWhichAndOffersIt()
    {
        _updates.Result = new UpdateCheckResult(true, "2.0.0", "https://github.com/DotifyBIZ/upkeep/releases/tag/v2.0.0");
        var viewModel = await LoadedAsync();

        await viewModel.CheckForUpdatesNowAsync(CancellationToken.None);

        Assert.Contains("SettingsUpdateAvailableFormat", viewModel.UpdateStatus, StringComparison.Ordinal);
        Assert.True(viewModel.HasRelease);
    }

    [Fact]
    public async Task CheckForUpdatesNowAsync_AlreadyCurrent_SaysSo()
    {
        var viewModel = await LoadedAsync();

        await viewModel.CheckForUpdatesNowAsync(CancellationToken.None);

        Assert.Equal("SettingsUpdateUpToDate", viewModel.UpdateStatus);
        Assert.False(viewModel.HasRelease);
    }

    [Fact]
    public async Task CheckForUpdatesNowAsync_Offline_IsAPlainMessageNotAnError()
    {
        // Upkeep works offline; a failed check is a normal outcome.
        _updates.ThrowNetworkFailure = true;
        var viewModel = await LoadedAsync();

        await viewModel.CheckForUpdatesNowAsync(CancellationToken.None);

        Assert.Equal("SettingsUpdateCheckFailed", viewModel.UpdateStatus);
        Assert.False(viewModel.IsCheckingForUpdates);
    }

    [Fact]
    public async Task CheckForUpdatesNowAsync_RunsOnDemandWhateverTheWeeklySettingSays()
    {
        _settings.Settings = new AppSettings { CheckForUpdatesEnabled = false };
        var viewModel = await LoadedAsync();

        await viewModel.CheckForUpdatesNowAsync(CancellationToken.None);

        Assert.Equal(1, _updates.CheckCount);
    }

    [Fact]
    public async Task OpenRelease_OnlyOpensOnceACheckFoundOne()
    {
        var viewModel = await LoadedAsync();

        viewModel.OpenRelease();

        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task OpenRelease_AfterACheckFoundOne_OpensThatRelease()
    {
        const string url = "https://github.com/DotifyBIZ/upkeep/releases/tag/v2.0.0";
        _updates.Result = new UpdateCheckResult(true, "2.0.0", url);
        var viewModel = await LoadedAsync();
        await viewModel.CheckForUpdatesNowAsync(CancellationToken.None);

        viewModel.OpenRelease();

        Assert.Equal(url, Assert.Single(_launcher.Opened).Target);
    }

    [Fact]
    public async Task EmptyQuarantineNow_DeletesItAndSaysHowMuchWentAway()
    {
        _quarantine.TotalBytes = 8192;
        var viewModel = await LoadedAsync();

        viewModel.EmptyQuarantineNow();

        Assert.True(_quarantine.WasPurged);
        Assert.Contains("SettingsQuarantineEmptiedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenLogsFolder_HandsTheFolderToWindows()
    {
        var viewModel = await LoadedAsync();

        viewModel.OpenLogsFolder();

        Assert.Single(_launcher.Opened);
    }

    [Fact]
    public async Task OpenProjectPage_OpensTheRepository()
    {
        var viewModel = await LoadedAsync();

        viewModel.OpenProjectPage();

        Assert.Equal(SettingsViewModel.ProjectUrl, Assert.Single(_launcher.Opened).Target);
    }

    [Fact]
    public async Task Open_WindowsWouldNotOpenIt_SaysSo()
    {
        _launcher.Succeeds = false;
        var viewModel = await LoadedAsync();

        viewModel.OpenProjectPage();

        Assert.Equal("SettingsCouldNotOpen", viewModel.StatusMessage);
    }

    [Fact]
    public void VersionDisplay_StampedBuild_ShowsTheVersion()
    {
        Assert.Contains("SettingsVersionFormat", CreateViewModel("1.2.3").VersionDisplay, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.0.0")]
    [InlineData("0.0.0.0")]
    [InlineData("")]
    public void VersionDisplay_UnstampedLocalBuild_SaysSoRatherThanPretending(string version)
    {
        // semantic-release stamps the version at publish time; a local build has none worth showing.
        Assert.Equal("SettingsVersionUnknown", CreateViewModel(version).VersionDisplay);
    }

    [Fact]
    public void VersionDisplay_DropsTheSourceRevisionSuffix()
    {
        Assert.Contains("1.4.0", CreateViewModel("1.4.0+abc1234").VersionDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("abc1234", CreateViewModel("1.4.0+abc1234").VersionDisplay, StringComparison.Ordinal);
    }
}
