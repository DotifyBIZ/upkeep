using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Management;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Safety;

/// <summary>
/// Creates restore points through Windows' own System Restore API (the <c>root\default</c>
/// <c>SystemRestore</c> WMI class). Runs **inside the elevated helper** — creating a restore point
/// needs administrator rights, and so does turning System Protection on.
/// <para>
/// Per the product decision in ADR-0006, protection is turned on if it is off rather than skipping
/// the safety net or blocking the run; the fact that Upkeep did so is reported afterwards, on the
/// results screen and in the session.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Calls Windows' System Restore WMI provider; the decisions it makes live in RestorePointPolicy, which is tested.")]
public sealed class SystemRestoreService : IRestorePointService
{
    private const string ScopePath = @"\\.\root\default";
    private const string ClassName = "SystemRestore";

    /// <summary>MODIFY_SETTINGS — what Windows' own tools use for maintenance changes.</summary>
    private const uint RestorePointTypeModifySettings = 12;

    /// <summary>BEGIN_SYSTEM_CHANGE.</summary>
    private const uint EventTypeBeginSystemChange = 100;

    private readonly IWellKnownPaths _paths;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public SystemRestoreService(IWellKnownPaths paths, IAppLogger logger, TimeProvider? timeProvider = null)
    {
        _paths = paths;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<bool> IsProtectionEnabledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsProtectionEnabled());

    public async Task<RestorePointResult> EnsureRestorePointAsync(string description, CancellationToken cancellationToken = default)
    {
        try
        {
            bool wasEnabled = IsProtectionEnabled();
            if (!wasEnabled)
            {
                EnableProtection();
            }

            var latestBefore = GetLatestRestorePointTime();
            bool created = TryCreateRestorePoint(description);
            var latestAfter = GetLatestRestorePointTime();

            // Windows reports success even when its 24-hour throttle means nothing was written, so
            // "did a new one appear" is the question, not "did the call return zero".
            bool actuallyCreated = created && latestAfter > latestBefore;
            var status = RestorePointPolicy.Evaluate(actuallyCreated, latestAfter, _timeProvider.GetUtcNow());

            string? reportedDescription = status switch
            {
                RestorePointStatus.Created => description,
                RestorePointStatus.ReusedRecent => GetLatestRestorePointDescription() ?? description,
                _ => null,
            };

            if (status == RestorePointStatus.Unavailable)
            {
                await _logger.LogWarningAsync("System Restore did not produce a restore point for this run.", cancellationToken);
            }

            return new RestorePointResult(status, reportedDescription, ProtectionWasEnabled: !wasEnabled);
        }
        catch (ManagementException ex)
        {
            await _logger.LogWarningAsync($"System Restore refused: {ex.Message}", cancellationToken);
            return new RestorePointResult(RestorePointStatus.Unavailable);
        }
        catch (UnauthorizedAccessException ex)
        {
            await _logger.LogWarningAsync($"System Restore needs administrator rights: {ex.Message}", cancellationToken);
            return new RestorePointResult(RestorePointStatus.NotElevated);
        }
    }

    /// <summary>
    /// Windows records protected volumes under SPP\Clients; an empty or missing value means System
    /// Protection is off for every drive, which is the default on a lot of Windows 11 machines.
    /// </summary>
    private static bool IsProtectionEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients");

            if (key is null)
            {
                return false;
            }

            foreach (string valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is string[] volumes && volumes.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void EnableProtection()
    {
        using var systemRestore = new ManagementClass(new ManagementScope(ScopePath), new ManagementPath(ClassName), null);
        using var parameters = systemRestore.GetMethodParameters("Enable");
        parameters["Drive"] = _paths.SystemDriveRoot;
        parameters["WaitForFrozenEvents"] = true;

        systemRestore.InvokeMethod("Enable", parameters, null);
    }

    private static bool TryCreateRestorePoint(string description)
    {
        using var systemRestore = new ManagementClass(new ManagementScope(ScopePath), new ManagementPath(ClassName), null);
        using var parameters = systemRestore.GetMethodParameters("CreateRestorePoint");
        parameters["Description"] = description;
        parameters["RestorePointType"] = RestorePointTypeModifySettings;
        parameters["EventType"] = EventTypeBeginSystemChange;

        using var result = systemRestore.InvokeMethod("CreateRestorePoint", parameters, null);
        return result is not null && Convert.ToUInt32(result["ReturnValue"], CultureInfo.InvariantCulture) == 0;
    }

    private static DateTimeOffset? GetLatestRestorePointTime() => GetLatestRestorePoint()?.CreatedAt;

    private static string? GetLatestRestorePointDescription() => GetLatestRestorePoint()?.Description;

    private static (DateTimeOffset CreatedAt, string? Description)? GetLatestRestorePoint()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(ScopePath), new ObjectQuery("SELECT * FROM SystemRestore"));
            using var results = searcher.Get();

            (DateTimeOffset CreatedAt, string? Description)? latest = null;
            foreach (ManagementObject point in results.Cast<ManagementObject>())
            {
                using (point)
                {
                    if (point["CreationTime"] is not string raw)
                    {
                        continue;
                    }

                    var createdAt = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(raw));
                    if (latest is null || createdAt > latest.Value.CreatedAt)
                    {
                        latest = (createdAt, point["Description"] as string);
                    }
                }
            }

            return latest;
        }
        catch (Exception ex) when (ex is ManagementException or FormatException)
        {
            return null;
        }
    }
}
