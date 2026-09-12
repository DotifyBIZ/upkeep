using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Cleanup;

public class SystemJunkScannerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeWellKnownPaths _paths = new();

    private SystemJunkScanner CreateScanner() => new(_paths, new FixedTimeProvider(Now));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _paths.Dispose();
    }

    [Fact]
    public void Scan_UserScopedCategory_IsRefused()
    {
        // The helper runs as administrator; walking a user's own folders is not its job, and with
        // over-the-shoulder elevation it would be the wrong user's folders anyway.
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateScanner().Scan(JunkCategoryId.UserTemp));
    }

    [Fact]
    public void Scan_WindowsTemp_AppliesTheSameGracePeriodAsTheUsersTemp()
    {
        string windowsTemp = Path.Combine(_paths.WindowsDirectory, "Temp");
        FakeWellKnownPaths.WriteFile(Path.Combine(windowsTemp, "old.tmp"), 400, Now.AddDays(-2));
        FakeWellKnownPaths.WriteFile(Path.Combine(windowsTemp, "busy.tmp"), 700, Now.AddMinutes(-1));

        var scan = CreateScanner().Scan(JunkCategoryId.WindowsTemp);

        Assert.Equal(400, scan.TotalBytes);
        Assert.Equal(1, scan.SkippedCount);
    }

    [Fact]
    public void Scan_OtherUsersTemp_LeavesOutTheAccountUsingUpkeep()
    {
        string mine = _paths.CreateUnder(Path.Combine("Users", "tester"));
        FakeWellKnownPaths.WriteFile(Path.Combine(mine, "AppData", "Local", "Temp", "mine.tmp"), 100, Now.AddDays(-3));
        string theirs = _paths.CreateUnder(Path.Combine("Users", "someone-else"));
        FakeWellKnownPaths.WriteFile(Path.Combine(theirs, "AppData", "Local", "Temp", "theirs.tmp"), 250, Now.AddDays(-3));

        var scan = CreateScanner().Scan(JunkCategoryId.OtherUsersTemp, excludeProfilePath: mine);

        Assert.Equal(250, scan.TotalBytes);
        Assert.Equal(["someone-else"], scan.Details);
    }

    [Theory]
    [InlineData("Public")]
    [InlineData("Default")]
    [InlineData("All Users")]
    public void Scan_OtherUsersTemp_SkipsProfilesThatBelongToNobody(string profileName)
    {
        string profile = _paths.CreateUnder(Path.Combine("Users", profileName));
        FakeWellKnownPaths.WriteFile(Path.Combine(profile, "AppData", "Local", "Temp", "x.tmp"), 500, Now.AddDays(-3));

        var scan = CreateScanner().Scan(JunkCategoryId.OtherUsersTemp);

        Assert.Equal(0, scan.TotalBytes);
    }

    [Fact]
    public void Scan_SystemCrashDumps_IncludesTheFullMemoryDump()
    {
        // MEMORY.DMP is a single file and is usually the largest thing in the category by far.
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.WindowsDirectory, "MEMORY.DMP"), 5000, Now);
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.WindowsDirectory, "Minidump", "091226-01.dmp"), 300, Now);
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.WindowsDirectory, "LiveKernelReports", "report.dmp"), 200, Now);
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.ProgramData, "Microsoft", "Windows", "WER", "ReportQueue", "r", "Report.wer"), 50, Now);

        var scan = CreateScanner().Scan(JunkCategoryId.SystemCrashDumps);

        Assert.Equal(5550, scan.TotalBytes);
        Assert.Equal(4, scan.ItemCount);
    }

    [Fact]
    public void Scan_DeliveryOptimization_SumsBothCacheLocations()
    {
        FakeWellKnownPaths.WriteFile(
            Path.Combine(_paths.WindowsDirectory, "SoftwareDistribution", "DeliveryOptimization", "a.bin"), 1000, Now);
        FakeWellKnownPaths.WriteFile(
            Path.Combine(_paths.WindowsDirectory, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "b.bin"), 500, Now);

        var scan = CreateScanner().Scan(JunkCategoryId.DeliveryOptimization);

        Assert.Equal(1500, scan.TotalBytes);
    }

    [Fact]
    public void Scan_WindowsUpdateCleanup_SaysTheSizeIsOnlyKnownAfterwards()
    {
        // DISM decides what is superseded and only reports afterwards; a number here would be a
        // guess presented as a promise.
        var scan = CreateScanner().Scan(JunkCategoryId.WindowsUpdateCleanup);

        Assert.Equal(JunkScanNote.SizeKnownAfterCleanup, scan.Note);
        Assert.Equal(0, scan.TotalBytes);
        Assert.False(scan.IsEmpty);
    }

    [Fact]
    public void Scan_NothingPresent_ReportsEmptyRatherThanFailing()
    {
        var scan = CreateScanner().Scan(JunkCategoryId.SystemCrashDumps);

        Assert.True(scan.IsEmpty);
    }
}
