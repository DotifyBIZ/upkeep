using CommunityToolkit.Mvvm.Messaging;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Safety;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class CleanupViewModelTests
{
    private readonly FakeJunkScanner _scanner = new();
    private readonly FakeCleanupExecutor _executor = new();
    private readonly FakeElevationService _elevation = new();
    private readonly WeakReferenceMessenger _messenger = new();

    private CleanupViewModel CreateViewModel() =>
        new(_scanner, _executor, _elevation, new FakeLocalizationService(), new FakeAppLogger(), _messenger);

    private static JunkCategoryScan ScanWith(JunkCategoryId id, long bytes, int items = 1) =>
        new(id, [.. Enumerable.Range(0, items).Select(index => new JunkItem($@"C:\temp\{id}-{index}", bytes / Math.Max(items, 1)))]);

    [Fact]
    public async Task ScanAsync_SplitsCategoriesIntoTheTwoGroupsThePreviewShows()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 1000));
        var viewModel = CreateViewModel();

        await viewModel.ScanAsync(CancellationToken.None);

        Assert.True(viewModel.HasScanned);
        Assert.Contains(viewModel.UserCategories, category => category.CategoryId == JunkCategoryId.UserTemp);
        Assert.Equal(JunkCatalog.ForScope(JunkScope.System).Count(), viewModel.SystemCategories.Count);
    }

    [Fact]
    public async Task ScanAsync_MachineWideCategoriesStartUnmeasuredAndUnselectable()
    {
        // They are listed so the user can see what exists, but nothing can be ticked before it has
        // a size and a file list to approve.
        var viewModel = CreateViewModel();

        await viewModel.ScanAsync(CancellationToken.None);

        Assert.All(viewModel.SystemCategories, category =>
        {
            Assert.False(category.IsSelected);
            Assert.False(category.IsSelectable);
        });
    }

    [Fact]
    public async Task ScanAsync_SelectsTheCategoriesTheCatalogMarksAsDefault()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 500));
        var viewModel = CreateViewModel();

        await viewModel.ScanAsync(CancellationToken.None);

        Assert.True(viewModel.UserCategories.Single(category => category.CategoryId == JunkCategoryId.UserTemp).IsSelected);
        Assert.True(viewModel.CanClean);
    }

    [Fact]
    public async Task ScanAsync_EmptyCategory_IsListedButNotSelected()
    {
        _scanner.UserScans.Add(JunkCategoryScan.Empty(JunkCategoryId.ShaderCache));
        var viewModel = CreateViewModel();

        await viewModel.ScanAsync(CancellationToken.None);

        var shaderCache = viewModel.UserCategories.Single(category => category.CategoryId == JunkCategoryId.ShaderCache);
        Assert.False(shaderCache.IsSelected);
        Assert.False(shaderCache.IsSelectable);
    }

    [Fact]
    public async Task BuildPlan_ContainsOnlyWhatIsStillTicked()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 1000));
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.ShaderCache, 400));
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        viewModel.UserCategories.Single(category => category.CategoryId == JunkCategoryId.ShaderCache).IsSelected = false;
        var plan = viewModel.BuildPlan();

        Assert.Single(plan.Categories);
        Assert.Equal(JunkCategoryId.UserTemp, plan.Categories[0].Scan.CategoryId);
    }

    [Fact]
    public async Task UntickingEverything_LeavesNothingToClean()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 1000));
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        foreach (var category in viewModel.UserCategories)
        {
            category.IsSelected = false;
        }

        Assert.False(viewModel.CanClean);
    }

    [Fact]
    public async Task IncludeSystemItemsAsync_ElevationDeclined_SaysSoAndChangesNothing()
    {
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        await viewModel.IncludeSystemItemsAsync(CancellationToken.None);

        Assert.False(viewModel.SystemItemsIncluded);
        Assert.True(viewModel.HasStatusMessage);
        Assert.Empty(_elevation.SentRequests);
    }

    [Fact]
    public async Task IncludeSystemItemsAsync_MeasuresEveryMachineWideCategoryThroughTheHelper()
    {
        _elevation.SetSystemScan(ScanWith(JunkCategoryId.WindowsTemp, 4096));
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        await viewModel.IncludeSystemItemsAsync(CancellationToken.None);

        Assert.True(viewModel.SystemItemsIncluded);
        Assert.False(viewModel.CanIncludeSystemItems);
        Assert.Equal(
            JunkCatalog.ForScope(JunkScope.System).Count(),
            _elevation.SentRequests.OfType<ScanJunkCategoryRequest>().Count());

        var windowsTemp = viewModel.SystemCategories.Single(category => category.CategoryId == JunkCategoryId.WindowsTemp);
        Assert.Equal(4096, windowsTemp.TotalBytes);
        Assert.True(windowsTemp.IsSelectable);
    }

    [Fact]
    public async Task CleanAsync_ShowsTheResultsInsteadOfThePreview()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 2048));
        _executor.Outcome = new CleanupOutcome
        {
            SessionId = "20260912-200000-abcdef12",
            Categories = [new CategoryOutcome(JunkCategoryId.UserTemp, 2048, 4, 1)],
            SystemDriveFreeBytes = 1024,
            RestorePoint = new RestorePointResult(RestorePointStatus.Created, "Upkeep cleanup"),
        };
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        await viewModel.CleanAsync(CancellationToken.None);

        Assert.True(viewModel.ShowResults);
        Assert.False(viewModel.ShowPreview);
        Assert.Equal("4", viewModel.ItemsRemovedDisplay);
        Assert.Equal("1", viewModel.ItemsSkippedDisplay);
        Assert.True(viewModel.HasRestorePointNote);
    }

    [Fact]
    public async Task CleanAsync_ReusedRestorePoint_SaysItWasAnExistingOne()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 10));
        _executor.Outcome = new CleanupOutcome
        {
            SessionId = "20260912-200000-abcdef12",
            Categories = [],
            RestorePoint = new RestorePointResult(RestorePointStatus.ReusedRecent, "Windows Update"),
        };
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        await viewModel.CleanAsync(CancellationToken.None);

        Assert.Contains("CleanupResultsRestorePointReusedFormat", viewModel.RestorePointDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanAsync_ElevationWasDeclinedDuringTheRun_TellsTheUserAfterwards()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 10));
        _executor.Outcome = new CleanupOutcome
        {
            SessionId = "20260912-200000-abcdef12",
            Categories = [],
            ElevationDeclined = true,
        };
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        await viewModel.CleanAsync(CancellationToken.None);

        Assert.Equal("CommonElevationDeclined", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CleanAsync_NothingSelected_DoesNotRunAnything()
    {
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        await viewModel.CleanAsync(CancellationToken.None);

        Assert.Null(_executor.ExecutedPlan);
        Assert.False(viewModel.ShowResults);
    }

    [Fact]
    public async Task DoneAsync_ReturnsToAFreshPreview()
    {
        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 10));
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);
        await viewModel.CleanAsync(CancellationToken.None);

        await viewModel.DoneAsync(CancellationToken.None);

        Assert.False(viewModel.ShowResults);
        Assert.True(viewModel.ShowPreview);
        Assert.True(viewModel.HasScanned);
    }

    [Fact]
    public void BeforeAnyScan_ThePageOffersToScanAndNothingElse()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.ShowScanPrompt);
        Assert.False(viewModel.CanIncludeSystemItems);
        Assert.False(viewModel.CanClean);
    }

    [Fact]
    public async Task CleanAsync_TellsTheShellTheRunFinished()
    {
        // The shell decides whether that is worth a toast — a run can outlive the page that
        // started it, and only the shell knows where the user went.
        var recipient = new object();
        CleanupFinishedMessage? received = null;
        _messenger.Register<CleanupFinishedMessage>(recipient, (_, message) => received = message);

        _scanner.UserScans.Add(ScanWith(JunkCategoryId.UserTemp, 2048));
        var viewModel = CreateViewModel();
        await viewModel.ScanAsync(CancellationToken.None);

        await viewModel.CleanAsync(CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal(viewModel.FreedDisplay, received.FreedSummary);
        GC.KeepAlive(recipient);
    }

    [Fact]
    public async Task CleanAsync_NothingSelected_SaysNothingToTheShell()
    {
        var recipient = new object();
        bool told = false;
        _messenger.Register<CleanupFinishedMessage>(recipient, (_, _) => told = true);

        var viewModel = CreateViewModel();
        await viewModel.CleanAsync(CancellationToken.None);

        Assert.False(told);
        GC.KeepAlive(recipient);
    }
}
