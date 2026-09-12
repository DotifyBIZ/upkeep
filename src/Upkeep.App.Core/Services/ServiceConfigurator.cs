using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Services;

/// <summary>The start types Upkeep will set. Boot and System are deliberately absent — nothing
/// here should ever promote a service into starting before Windows is up.</summary>
public enum ServiceStartType
{
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
}

/// <summary>Outcome of a start-type change, with a code the shell can localize.</summary>
public sealed record ServiceChangeResult(bool Success, string? FailureCode = null, string? Detail = null)
{
    public static readonly ServiceChangeResult Ok = new(true);
}

/// <summary>
/// Changes a service's start type, inside the elevated helper.
/// <para>
/// The helper re-runs the classification itself before touching anything (ADR-0005): the shell
/// asking to disable a service is not authority to do it, and a locked service stays locked no
/// matter what arrives over the pipe. `sc.exe` does the actual write — Windows' own tool, and its
/// exit code is the only thing read, because its output is localized.
/// </para>
/// </summary>
public sealed class ServiceConfigurator
{
    private static readonly TimeSpan ConfigureTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceScanner _scanner;
    private readonly IWindowsToolRunner _toolRunner;
    private readonly IWellKnownPaths _paths;
    private readonly IAppLogger _logger;

    public ServiceConfigurator(IServiceScanner scanner, IWindowsToolRunner toolRunner, IWellKnownPaths paths, IAppLogger logger)
    {
        _scanner = scanner;
        _toolRunner = toolRunner;
        _paths = paths;
        _logger = logger;
    }

    public async Task<ServiceChangeResult> SetStartTypeAsync(string serviceName, ServiceStartType startType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // Re-derived here, not taken from the request: this is the check that makes the closed
        // operation set meaningful.
        var services = await _scanner.ScanAsync(cancellationToken);
        var service = services.FirstOrDefault(candidate => candidate.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

        if (service is null)
        {
            return new ServiceChangeResult(false, "service_not_found", serviceName);
        }

        if (!ServiceClassifier.CanChange(service))
        {
            await _logger.LogWarningAsync($"Refused to change {serviceName}: Windows needs it.", cancellationToken);
            return new ServiceChangeResult(false, "service_locked", serviceName);
        }

        string sc = Path.Combine(_paths.WindowsDirectory, "System32", "sc.exe");
        var result = await _toolRunner.RunAsync(
            sc,
            ["config", serviceName, "start=", ToScValue(startType)],
            ConfigureTimeout,
            cancellationToken);

        if (result.Succeeded)
        {
            return ServiceChangeResult.Ok;
        }

        await _logger.LogWarningAsync($"sc.exe refused to configure {serviceName} (exit {result.ExitCode}).", cancellationToken);
        return new ServiceChangeResult(false, "windows_refused", $"sc.exe exited with {result.ExitCode}");
    }

    /// <summary>
    /// sc.exe's own vocabulary. "demand" rather than "manual", and "delayed-auto" as one token —
    /// getting either wrong means a service that silently keeps its old start type.
    /// </summary>
    public static string ToScValue(ServiceStartType startType) => startType switch
    {
        ServiceStartType.Automatic => "auto",
        ServiceStartType.AutomaticDelayed => "delayed-auto",
        ServiceStartType.Manual => "demand",
        ServiceStartType.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(startType), startType, "Unsupported start type."),
    };

    /// <summary>Maps what WMI reported back to the enum, for recording the previous value.</summary>
    public static ServiceStartType FromReportedStartType(string? startType) => startType switch
    {
        "Automatic" => ServiceStartType.Automatic,
        "Disabled" => ServiceStartType.Disabled,
        _ => ServiceStartType.Manual,
    };
}
