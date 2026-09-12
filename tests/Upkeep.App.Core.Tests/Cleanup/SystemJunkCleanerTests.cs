using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Tests.Fakes;

namespace Upkeep.App.Core.Tests.Cleanup;

public class SystemJunkCleanerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeWellKnownPaths _paths = new();
    private readonly FakeWindowsToolRunner _toolRunner = new();
    private readonly FileAppLogger _logger;
    private readonly SystemJunkCleaner _cleaner;

    public SystemJunkCleanerTests()
    {
        _logger = new FileAppLogger(Path.Combine(Path.GetTempPath(), $"upkeep-cleaner-{Guid.NewGuid():N}"));
        var scanner = new SystemJunkScanner(_paths, new FixedTimeProvider(Now));
        _cleaner = new SystemJunkCleaner(scanner, _toolRunner, _paths, _logger);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _logger.Dispose();
        _paths.Dispose();

        try
        {
            Directory.Delete(_logger.LogDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string WriteWindowsTempFile(string name, int size) =>
        FakeWellKnownPaths.WriteFile(Path.Combine(_paths.WindowsDirectory, "Temp", name), size, Now.AddDays(-3));

    [Fact]
    public async Task CleanAsync_UserScopeCategory_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _cleaner.CleanAsync(JunkCategoryId.UserTemp, [], null, CancellationToken.None));
    }

    [Fact]
    public async Task CleanAsync_RemovesTheApprovedFiles()
    {
        string approved = WriteWindowsTempFile("a.tmp", 400);

        var outcome = await _cleaner.CleanAsync(JunkCategoryId.WindowsTemp, [approved], null, CancellationToken.None);

        Assert.Equal(400, outcome.FreedBytes);
        Assert.Equal(1, outcome.ItemsRemoved);
        Assert.False(File.Exists(approved));
    }

    [Fact]
    public async Task CleanAsync_LeavesFilesTheUserDidNotApprove()
    {
        // The request can narrow what the helper deletes — someone unticked a category, or the
        // preview was taken before a file appeared.
        string approved = WriteWindowsTempFile("approved.tmp", 100);
        string notApproved = WriteWindowsTempFile("not-approved.tmp", 900);

        var outcome = await _cleaner.CleanAsync(JunkCategoryId.WindowsTemp, [approved], null, CancellationToken.None);

        Assert.Equal(100, outcome.FreedBytes);
        Assert.True(File.Exists(notApproved));
    }

    [Fact]
    public async Task CleanAsync_IgnoresPathsTheHelpersOwnScanDidNotProduce()
    {
        // The heart of ADR-0005: whatever arrives in the request, the helper acts only on the
        // intersection with what it derived itself. A path outside the category is not touched.
        string outsideTheCategory = FakeWellKnownPaths.WriteFile(Path.Combine(_paths.UserProfile, "Documents", "thesis.docx"), 5000);

        var outcome = await _cleaner.CleanAsync(
            JunkCategoryId.WindowsTemp,
            [outsideTheCategory, @"C:\Windows\System32\kernel32.dll"],
            null,
            CancellationToken.None);

        Assert.Equal(0, outcome.FreedBytes);
        Assert.Equal(0, outcome.ItemsRemoved);
        Assert.True(File.Exists(outsideTheCategory));
    }

    [Fact]
    public async Task CleanAsync_FileAlreadyGone_IsCountedAsSkipped()
    {
        string file = WriteWindowsTempFile("vanishing.tmp", 100);
        var approved = new[] { file };
        File.Delete(file);

        var outcome = await _cleaner.CleanAsync(JunkCategoryId.WindowsTemp, approved, null, CancellationToken.None);

        Assert.Equal(0, outcome.FreedBytes);
        Assert.Equal(0, outcome.ItemsRemoved);
    }

    [Fact]
    public async Task CleanAsync_OtherUsersTemp_LeavesTheCurrentUsersFilesAlone()
    {
        string mine = _paths.CreateUnder(Path.Combine("Users", "tester"));
        string myFile = FakeWellKnownPaths.WriteFile(Path.Combine(mine, "AppData", "Local", "Temp", "mine.tmp"), 100, Now.AddDays(-3));
        string theirs = _paths.CreateUnder(Path.Combine("Users", "someone-else"));
        string theirFile = FakeWellKnownPaths.WriteFile(Path.Combine(theirs, "AppData", "Local", "Temp", "theirs.tmp"), 200, Now.AddDays(-3));

        var outcome = await _cleaner.CleanAsync(
            JunkCategoryId.OtherUsersTemp,
            [myFile, theirFile],
            excludeProfilePath: mine,
            CancellationToken.None);

        Assert.Equal(200, outcome.FreedBytes);
        Assert.True(File.Exists(myFile));
        Assert.False(File.Exists(theirFile));
    }

    [Fact]
    public async Task CleanAsync_WindowsUpdateCleanup_RunsDismWithoutResetBase()
    {
        var outcome = await _cleaner.CleanAsync(JunkCategoryId.WindowsUpdateCleanup, [], null, CancellationToken.None);

        var invocation = Assert.Single(_toolRunner.Invocations);
        Assert.EndsWith("Dism.exe", invocation.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/StartComponentCleanup", invocation.Arguments);
        // /ResetBase would make every installed update permanently uninstallable.
        Assert.DoesNotContain("/ResetBase", invocation.Arguments);
        Assert.Equal(1, outcome.ItemsRemoved);
    }

    [Fact]
    public async Task CleanAsync_DismFails_ReportsItRatherThanClaimingSuccess()
    {
        _toolRunner.Result = new Platform.ToolResult(87, "Error: 87");

        var outcome = await _cleaner.CleanAsync(JunkCategoryId.WindowsUpdateCleanup, [], null, CancellationToken.None);

        Assert.Equal(0, outcome.ItemsRemoved);
        Assert.Equal(1, outcome.ItemsSkipped);
    }

    [Fact]
    public async Task CleanAsync_DeliveryOptimization_UsesWindowsOwnCmdlet()
    {
        FakeWellKnownPaths.WriteFile(
            Path.Combine(_paths.WindowsDirectory, "SoftwareDistribution", "DeliveryOptimization", "a.bin"), 1000, Now);

        await _cleaner.CleanAsync(JunkCategoryId.DeliveryOptimization, [], null, CancellationToken.None);

        var invocation = Assert.Single(_toolRunner.Invocations);
        Assert.EndsWith("powershell.exe", invocation.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(invocation.Arguments, argument => argument.Contains("Delete-DeliveryOptimizationCache", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CleanAsync_DeliveryOptimizationCmdletFails_IsReportedAsSkipped()
    {
        _toolRunner.Result = new Platform.ToolResult(1, "The term is not recognized.");

        var outcome = await _cleaner.CleanAsync(JunkCategoryId.DeliveryOptimization, [], null, CancellationToken.None);

        Assert.Equal(0, outcome.FreedBytes);
        Assert.Equal(1, outcome.ItemsSkipped);
    }
}
