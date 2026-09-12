using Upkeep.App.Core.Apps;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class AppsViewModelTests
{
    private readonly FakeInstalledAppScanner _scanner = new();
    private readonly FakeAppUninstaller _uninstaller = new();
    private readonly FakeLeftoverRemover _leftoverRemover = new();

    private AppsViewModel CreateViewModel() =>
        new(_scanner, _uninstaller, _leftoverRemover, new FakeLocalizationService(), new FakeAppLogger());

    private static InstalledApp App(
        string name = "Zoom Workplace",
        string? publisher = "Zoom Communications",
        AppSource source = AppSource.Desktop,
        long? sizeBytes = 298 * 1024 * 1024L) => new()
        {
            Id = name,
            DisplayName = name,
            Publisher = publisher,
            Version = "6.5.3",
            EstimatedBytes = sizeBytes,
            Source = source,
            Scope = AppScope.CurrentUser,
            Uninstall = new UninstallCommand(@"C:\App\uninstall.exe", []),
        };

    [Fact]
    public async Task LoadAsync_ShowsEveryInstalledApp()
    {
        _scanner.Apps.Add(App());
        _scanner.Apps.Add(App("Discord", "Discord Inc."));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Apps.Count);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_FormatsWhatEachRowShows()
    {
        _scanner.Apps.Add(App());
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        var row = viewModel.Apps[0];
        Assert.Equal("Zoom Workplace", row.Name);
        Assert.Contains("Zoom Communications", row.Subtitle, StringComparison.Ordinal);
        Assert.Contains("6.5.3", row.Subtitle, StringComparison.Ordinal);
        Assert.Equal("Z", row.Initial);
    }

    [Fact]
    public async Task LoadAsync_AppWithNoRecordedSize_SaysSoRatherThanShowingZero()
    {
        _scanner.Apps.Add(App(sizeBytes: null));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("AppsSizeUnknown", viewModel.Apps[0].SizeDisplay);
    }

    [Fact]
    public async Task Filter_NarrowsByNameAndPublisher()
    {
        _scanner.Apps.Add(App());
        _scanner.Apps.Add(App("Discord", "Discord Inc."));
        _scanner.Apps.Add(App("Blender", "Blender Foundation"));
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.Filter("zoom");
        Assert.Single(viewModel.Apps);

        viewModel.Filter("Foundation");
        Assert.Equal("Blender", viewModel.Apps[0].Name);

        viewModel.Filter("");
        Assert.Equal(3, viewModel.Apps.Count);
    }

    [Fact]
    public async Task UninstallAsync_RemovedCleanly_DropsItFromTheListAndSaysSo()
    {
        _scanner.Apps.Add(App());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        var outcome = await viewModel.UninstallAsync(viewModel.Apps[0], CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.Empty(viewModel.Apps);
        Assert.Contains("AppsNoLeftoversFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UninstallAsync_LeftoversFound_KeepsThemForThePageToOffer()
    {
        _scanner.Apps.Add(App());
        _uninstaller.Outcome = new UninstallOutcome(
            UninstallLaunchStatus.Completed,
            StillInstalled: false,
            [new LeftoverItem(LeftoverKind.Folder, @"C:\Users\a\AppData\Roaming\Zoom", 184)]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        var outcome = await viewModel.UninstallAsync(viewModel.Apps[0], CancellationToken.None);

        Assert.Single(outcome!.Leftovers);
        // No "nothing left behind" message when there is something left behind.
        Assert.Null(viewModel.StatusMessage);
    }

    [Fact]
    public async Task UninstallAsync_UserCancelledTheUninstaller_KeepsTheAppListed()
    {
        _scanner.Apps.Add(App());
        _uninstaller.Outcome = new UninstallOutcome(UninstallLaunchStatus.Declined, StillInstalled: true, []);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.UninstallAsync(viewModel.Apps[0], CancellationToken.None);

        Assert.Single(viewModel.Apps);
        Assert.Equal("AppsUninstallDeclined", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UninstallAsync_AppStillInstalled_SaysSoInsteadOfClaimingSuccess()
    {
        _scanner.Apps.Add(App());
        _uninstaller.Outcome = new UninstallOutcome(UninstallLaunchStatus.Completed, StillInstalled: true, []);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.UninstallAsync(viewModel.Apps[0], CancellationToken.None);

        Assert.Single(viewModel.Apps);
        Assert.Contains("AppsStillInstalledFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UninstallAsync_UninstallerWouldNotStart_ReportsWhy()
    {
        _scanner.Apps.Add(App());
        _uninstaller.Outcome = new UninstallOutcome(UninstallLaunchStatus.Failed, StillInstalled: true, [], "File not found");
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.UninstallAsync(viewModel.Apps[0], CancellationToken.None);

        Assert.Contains("AppsUninstallFailedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveLeftoversAsync_PassesOnlyWhatItWasGiven()
    {
        _scanner.Apps.Add(App());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);
        LeftoverItem[] chosen = [new(LeftoverKind.Folder, @"C:\Users\a\AppData\Roaming\Zoom", 184)];
        _leftoverRemover.Outcome = new LeftoverRemovalOutcome("s", RemovedCount: 1, FailedCount: 0, QuarantinedBytes: 184);

        await viewModel.RemoveLeftoversAsync(viewModel.Apps[0], chosen, CancellationToken.None);

        Assert.Single(_leftoverRemover.RemovedItems);
        Assert.Contains("AppsLeftoversRemovedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveLeftoversAsync_NothingTicked_DoesNothingAtAll()
    {
        _scanner.Apps.Add(App());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.RemoveLeftoversAsync(viewModel.Apps[0], [], CancellationToken.None);

        Assert.Empty(_leftoverRemover.RemovedItems);
    }

    [Fact]
    public async Task RemoveLeftoversAsync_SomethingRefused_SaysHowMany()
    {
        _scanner.Apps.Add(App());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);
        _leftoverRemover.Outcome = new LeftoverRemovalOutcome("s", RemovedCount: 1, FailedCount: 2, QuarantinedBytes: 0);

        await viewModel.RemoveLeftoversAsync(
            viewModel.Apps[0],
            [new LeftoverItem(LeftoverKind.Folder, @"C:\ProgramData\Zoom", 4, RequiresElevation: true)],
            CancellationToken.None);

        Assert.Contains("AppsLeftoversFailedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }
}
