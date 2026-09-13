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
}
