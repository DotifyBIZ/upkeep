using Upkeep.App.Core.Logging;

namespace Upkeep.App.Core.Tests.Logging;

public class FileAppLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"upkeep-logger-{Guid.NewGuid():N}");

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public async Task LogInfoAsync_WritesTheLine()
    {
        using var logger = new FileAppLogger(_directory);

        await logger.LogInfoAsync("something happened");

        string written = string.Concat(Directory.EnumerateFiles(_directory).Select(File.ReadAllText));

        Assert.Contains("something happened", written, StringComparison.Ordinal);
        Assert.Contains("[INFO]", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogErrorAsync_TokenAlreadyCancelled_DoesNotThrow()
    {
        // The caller is usually already handling its own failure, and a logger that throws on the
        // way out of a cancelled operation replaces that failure with its own. This once took the
        // elevated helper down mid-cleanup.
        using var logger = new FileAppLogger(_directory);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await logger.LogErrorAsync("the operation was cancelled", null, cancelled.Token);
    }

    [Fact]
    public async Task LogWarningAsync_AfterDispose_DoesNotThrow()
    {
        // Shutdown order is not something a caller of a logger should have to reason about.
        var logger = new FileAppLogger(_directory);
        logger.Dispose();

        await logger.LogWarningAsync("logged on the way out");
    }

    [Fact]
    public async Task LogErrorAsync_DirectoryPathIsAFile_DoesNotThrow()
    {
        // Disk full, a stray file where the folder should be, a locked path: all the same answer.
        string path = Path.Combine(Path.GetTempPath(), $"upkeep-logger-file-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, "not a directory");

        try
        {
            using var logger = new FileAppLogger(path);
            await logger.LogErrorAsync("nowhere to write this");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadRecentAsync_NoLogsYet_ReturnsEmpty()
    {
        using var logger = new FileAppLogger(_directory);

        var lines = await logger.ReadRecentAsync(50);

        Assert.Empty(lines);
    }

    [Fact]
    public async Task ReadRecentAsync_ReturnsWhatWasLogged()
    {
        using var logger = new FileAppLogger(_directory);
        await logger.LogInfoAsync("first thing");
        await logger.LogWarningAsync("second thing");

        var lines = await logger.ReadRecentAsync(50);

        Assert.Equal(2, lines.Count);
        Assert.Contains("first thing", lines[0], StringComparison.Ordinal);
        Assert.Contains("second thing", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadRecentAsync_MoreLinesThanAskedFor_KeepsTheLastOnes()
    {
        using var logger = new FileAppLogger(_directory);
        for (int index = 0; index < 10; index++)
        {
            await logger.LogInfoAsync($"line {index}");
        }

        var lines = await logger.ReadRecentAsync(3);

        Assert.Equal(3, lines.Count);
        Assert.Contains("line 7", lines[0], StringComparison.Ordinal);
        Assert.Contains("line 9", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadRecentAsync_SeveralDaysOfLogs_ReadsTheNewestFile()
    {
        // Files roll daily; what a user wants to see is the latest one, not whichever the file
        // system happens to enumerate first.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "upkeep-2020-01-01.log"), $"old line{Environment.NewLine}");
        await File.WriteAllTextAsync(Path.Combine(_directory, "upkeep-2030-01-01.log"), $"new line{Environment.NewLine}");

        using var logger = new FileAppLogger(_directory);
        var lines = await logger.ReadRecentAsync(50);

        Assert.Equal("new line", Assert.Single(lines));
    }

    [Fact]
    public async Task ReadRecentAsync_ZeroOrFewerLines_ReturnsEmpty()
    {
        using var logger = new FileAppLogger(_directory);
        await logger.LogInfoAsync("something happened");

        Assert.Empty(await logger.ReadRecentAsync(0));
        Assert.Empty(await logger.ReadRecentAsync(-5));
    }
}
