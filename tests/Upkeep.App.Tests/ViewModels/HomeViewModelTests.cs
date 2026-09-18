using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Storage;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class HomeViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);

    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-home-vm-{Guid.NewGuid():N}");
    private readonly FakeDriveScanner _drives = new();
    private readonly FakeUpdateCheckService _updateCheck = new();
    private readonly SessionJournal _journal;

    public HomeViewModelTests() => _journal = new SessionJournal(_journalDirectory, new FixedTimeProvider(Now));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _journal.Dispose();

        try
        {
            Directory.Delete(_journalDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private HomeViewModel CreateViewModel() => new(
        _drives,
        _updateCheck,
        _journal,
        new FakeLocalizationService(),
        new FakeAppLogger(),
        new FixedTimeProvider(Now));

    private async Task<SessionManifest> WriteCompletedCleanupAsync(DateTimeOffset completedAt, long freedBytes = 0)
    {
        var manifest = await _journal.StartAsync(SessionKind.Cleanup);
        manifest = manifest with { StartedAt = completedAt, CompletedAt = completedAt };

        if (freedBytes > 0)
        {
            var entry = new FileDeletedEntry("C:\\Temp\\a.tmp", freedBytes) { Completed = true };
            manifest = await _journal.AppendAsync(manifest, entry);
        }
        else
        {
            manifest = await _journal.SaveAsync(manifest);
        }

        return manifest;
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(0.89, false)]
    [InlineData(0.90, true)]
    [InlineData(0.99, true)]
    public void IsDriveLow_ChecksTheNinetyPercentThreshold(double usedFraction, bool expected) =>
        Assert.Equal(expected, HomeViewModel.IsDriveLow(usedFraction));

    [Theory]
    [InlineData(13, false)]
    [InlineData(14, false)]
    [InlineData(15, true)]
    public void IsCleanupStale_ChecksTheFourteenDayThreshold(int daysAgo, bool expected) =>
        Assert.Equal(expected, HomeViewModel.IsCleanupStale(Now.AddDays(-daysAgo), Now));

    [Fact]
    public async Task LoadAsync_NoCleanupEverRan_ShowsAttentionAndSaysSo()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.IsHealthGood);
        Assert.True(viewModel.IsHealthAttention);
        Assert.Equal("HomeHealthNeverCleanedDetail", viewModel.HealthDetail);
    }

    [Fact]
    public async Task LoadAsync_RecentCleanup_ShowsGood()
    {
        await WriteCompletedCleanupAsync(Now.AddDays(-2), freedBytes: 1024);

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsHealthGood);
        Assert.StartsWith("HomeHealthGoodWithFreedFormat", viewModel.HealthDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RecentCleanupWithNothingFreed_OmitsTheFreedClause()
    {
        // A session can complete having found nothing worth removing; claiming a specific amount
        // was freed when it was zero would be a small lie in an otherwise honest status line.
        await WriteCompletedCleanupAsync(Now.AddDays(-1));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsHealthGood);
        Assert.StartsWith("HomeHealthGoodFormat", viewModel.HealthDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_StaleCleanup_ShowsAttention()
    {
        await WriteCompletedCleanupAsync(Now.AddDays(-30), freedBytes: 1024);

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.IsHealthGood);
        Assert.StartsWith("HomeHealthStaleFormat", viewModel.HealthDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_UnfinishedCleanup_DoesNotCountAsRecent()
    {
        // Started but never completed — the session exists, but nothing was actually cleaned.
        await _journal.StartAsync(SessionKind.Cleanup);

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.IsHealthGood);
    }

    [Fact]
    public async Task LoadAsync_NoDriveIsLow_HasNoNextAction()
    {
        _drives.Drives.Add(new DriveSnapshot("C:", "C:\\", 512_000_000_000, 200_000_000_000));

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.HasNextAction);
    }

    [Fact]
    public async Task LoadAsync_ADriveIsLow_OffersTheWorstOne()
    {
        _drives.Drives.Add(new DriveSnapshot("C:", "C:\\", 512_000_000_000, 200_000_000_000));
        _drives.Drives.Add(new DriveSnapshot("D:", "D:\\", 1_000_000_000_000, 20_000_000_000)); // 98% used

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.HasNextAction);
        Assert.Contains("D:", viewModel.NextActionTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_NoSessionsEver_RecentActivityIsEmptyAndSaysSo()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Empty(viewModel.RecentActivity);
        Assert.True(viewModel.HasNoRecentActivity);
    }

    [Fact]
    public void BeforeAnyLoad_DoesNotClaimThereIsNothing()
    {
        // The empty-state message would be a lie told before the journal was ever actually read.
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasNoRecentActivity);
    }

    [Fact]
    public async Task LoadAsync_SeveralSessions_ShowsTheThreeMostRecent()
    {
        for (int i = 0; i < 5; i++)
        {
            await WriteCompletedCleanupAsync(Now.AddDays(-i));
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(3, viewModel.RecentActivity.Count);
        Assert.False(viewModel.HasNoRecentActivity);
    }

    [Theory]
    [InlineData(5, "HomeGreetingMorning")]
    [InlineData(11, "HomeGreetingMorning")]
    [InlineData(12, "HomeGreetingAfternoon")]
    [InlineData(17, "HomeGreetingAfternoon")]
    [InlineData(18, "HomeGreetingEvening")]
    [InlineData(22, "HomeGreetingEvening")]
    [InlineData(23, "HomeGreetingNight")]
    [InlineData(0, "HomeGreetingNight")]
    [InlineData(4, "HomeGreetingNight")]
    public void GreetingKeyFor_PicksTheGreetingForThatHour(int hour, string expectedKey) =>
        Assert.Equal(expectedKey, HomeViewModel.GreetingKeyFor(hour));

    [Fact]
    public void GreetingKeyFor_CoversEveryHourOfTheDay()
    {
        // No hour may fall through to an empty greeting on the first screen of the app.
        for (int hour = 0; hour < 24; hour++)
        {
            Assert.False(string.IsNullOrEmpty(HomeViewModel.GreetingKeyFor(hour)));
        }
    }
}
