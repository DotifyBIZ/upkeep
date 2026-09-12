namespace Upkeep.App.Core.Logging;

/// <summary>
/// Local troubleshooting log — not telemetry. Nothing here is ever transmitted anywhere; it only
/// writes to a local file the user can find and share themselves. See
/// docs/adr/0003-local-diagnostic-logging.md.
/// </summary>
public interface IAppLogger
{
    /// <summary>The folder the log files live in — surfaced in Settings so a user can find them.</summary>
    string LogDirectory { get; }

    Task LogErrorAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default);

    Task LogWarningAsync(string message, CancellationToken cancellationToken = default);

    Task LogInfoAsync(string message, CancellationToken cancellationToken = default);
}
