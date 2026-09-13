using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Upkeep.App.Core.Logging;

/// <summary>
/// Appends timestamped lines to a daily-rolling text file under %LocalAppData%\Upkeep\Logs.
/// Never throws: a logging failure must never take down the caller, which is usually already
/// handling its own error.
/// </summary>
public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private static readonly TimeSpan LogRetention = TimeSpan.FromDays(30);

    private readonly string _logDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _prunedOldLogs;

    [ExcludeFromCodeCoverage(Justification = "Resolves a well-known Windows folder; the injectable constructor below is covered.")]
    public FileAppLogger()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Upkeep", "Logs"))
    {
    }

    public FileAppLogger(string logDirectory) => _logDirectory = logDirectory;

    public string LogDirectory => _logDirectory;

    public void Dispose() => _writeLock.Dispose();

    public Task LogErrorAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default) =>
        WriteAsync("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}", cancellationToken);

    public Task LogWarningAsync(string message, CancellationToken cancellationToken = default) =>
        WriteAsync("WARN", message, cancellationToken);

    public Task LogInfoAsync(string message, CancellationToken cancellationToken = default) =>
        WriteAsync("INFO", message, cancellationToken);

    private async Task WriteAsync(string level, string message, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            string filePath = Path.Combine(_logDirectory, $"upkeep-{DateTimeOffset.UtcNow:yyyy-MM-dd}.log");
            string line = $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}";

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(filePath, line, cancellationToken);
                PruneOldLogsOnce();
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException)
        {
            // Logging must never itself throw — a failure here costs this one line, not the
            // caller's own error handling. Cancellation counts: a caller that logs on the way out
            // of a cancelled operation passes the token that was just cancelled, and letting that
            // surface once took down the whole elevated helper mid-cleanup. Disposal counts for
            // the same reason, on the way through shutdown.
        }
    }

    // Daily-rolling files with nothing deleting them accumulate for the life of the install. Runs
    // once per process, under the write lock that already serializes this class's file access.
    private void PruneOldLogsOnce()
    {
        if (_prunedOldLogs)
        {
            return;
        }

        _prunedOldLogs = true;

        var cutoff = DateTime.UtcNow - LogRetention;
        foreach (string oldLog in Directory.EnumerateFiles(_logDirectory, "upkeep-*.log"))
        {
            if (File.GetLastWriteTimeUtc(oldLog) < cutoff)
            {
                File.Delete(oldLog);
            }
        }
    }
}
