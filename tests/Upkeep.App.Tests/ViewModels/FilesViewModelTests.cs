using Upkeep.App.Core.Files;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class FilesViewModelTests
{
    private static readonly DateTime Modified = new(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeDuplicateFinder _duplicates = new();
    private readonly FakeLargeFileFinder _largeFiles = new();

    private FilesViewModel CreateViewModel() => new(
        _duplicates,
        _largeFiles,
        new FakeDiskUsageScanner(),
        new FakeFileActionExecutor(),
        new FakeFolderPickerService(),
        new FakeLocalizationService(),
        new FakeAppLogger());

    private static LargeFileResult LargeFile(string path = @"C:\Users\Test\Videos\holiday.mp4") =>
        new(new ScannedFile(path, 4_000_000_000, Modified), IsLarge: true, IsOld: false);

    private static DuplicateGroup Group()
    {
        var keep = new ScannedFile(@"C:\Users\Test\a.jpg", 1024, Modified);
        return new DuplicateGroup([keep, new ScannedFile(@"C:\Users\Test\copy\a.jpg", 1024, Modified)], keep);
    }

    [Fact]
    public void BeforeAnyScan_NeitherEmptyMessageShows()
    {
        // Nothing has been looked for yet, which is not the same as having found nothing.
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasNoLargeFiles);
        Assert.False(viewModel.HasNoDuplicates);
    }

    [Fact]
    public async Task ScanLargeFilesAsync_FoundSomething_DoesNotAlsoSayItFoundNothing()
    {
        // The empty-state message sat above a table full of results, because it only asked whether
        // a scan had run.
        _largeFiles.Results.Add(LargeFile());

        var viewModel = CreateViewModel();
        await viewModel.ScanLargeFilesAsync(CancellationToken.None);

        Assert.Single(viewModel.LargeFiles);
        Assert.False(viewModel.HasNoLargeFiles);
    }

    [Fact]
    public async Task ScanLargeFilesAsync_FoundNothing_SaysSo()
    {
        var viewModel = CreateViewModel();
        await viewModel.ScanLargeFilesAsync(CancellationToken.None);

        Assert.Empty(viewModel.LargeFiles);
        Assert.True(viewModel.HasNoLargeFiles);
    }

    [Fact]
    public async Task ScanLargeFilesAsync_SecondScanFindsNothing_StopsShowingTheOldResults()
    {
        // The flag is already true by the second scan, so the message only updates if the change
        // is raised explicitly.
        _largeFiles.Results.Add(LargeFile());

        var viewModel = CreateViewModel();
        await viewModel.ScanLargeFilesAsync(CancellationToken.None);

        _largeFiles.Results.Clear();
        await viewModel.ScanLargeFilesAsync(CancellationToken.None);

        Assert.Empty(viewModel.LargeFiles);
        Assert.True(viewModel.HasNoLargeFiles);
    }

    [Fact]
    public async Task ScanDuplicatesAsync_FoundSomething_DoesNotAlsoSayItFoundNothing()
    {
        _duplicates.Groups.Add(Group());

        var viewModel = CreateViewModel();
        await viewModel.ScanDuplicatesAsync(CancellationToken.None);

        Assert.Single(viewModel.DuplicateGroups);
        Assert.False(viewModel.HasNoDuplicates);
    }

    [Fact]
    public async Task ScanDuplicatesAsync_FoundNothing_SaysSo()
    {
        var viewModel = CreateViewModel();
        await viewModel.ScanDuplicatesAsync(CancellationToken.None);

        Assert.Empty(viewModel.DuplicateGroups);
        Assert.True(viewModel.HasNoDuplicates);
    }
}
