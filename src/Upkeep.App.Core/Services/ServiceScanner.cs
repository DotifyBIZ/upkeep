using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Management;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Services;

/// <summary>Lists the Windows services on this machine, already classified.</summary>
public interface IServiceScanner
{
    Task<IReadOnlyList<ServiceInfo>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads services through WMI, including which ones other running services depend on.
/// <para>
/// A reading adapter: which tier a service lands in is decided by
/// <see cref="ServiceClassifier"/>, which is pure and tested. Drivers are filtered out entirely —
/// this page is about services, and a list that lets someone disable a storage driver is a list
/// that bricks machines.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads this machine's service database through WMI; the tiering decisions built on it are tested in ServiceClassifierTests.")]
public sealed class ServiceScanner : IServiceScanner
{
    private readonly IRegistryProbe _registry;

    public ServiceScanner(IRegistryProbe registry) => _registry = registry;

    public Task<IReadOnlyList<ServiceInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<ServiceInfo>>(
            () =>
            {
                var services = new List<ServiceInfo>();
                var runningDependencies = ReadRunningDependencies();

                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, DisplayName, StartMode, State, PathName, ServiceType FROM Win32_Service");
                    using var results = searcher.Get();

                    foreach (ManagementObject service in results.Cast<ManagementObject>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        using (service)
                        {
                            string name = service["Name"] as string ?? string.Empty;
                            if (string.IsNullOrEmpty(name))
                            {
                                continue;
                            }

                            services.Add(new ServiceInfo(
                                name,
                                service["DisplayName"] as string ?? name,
                                NormalizeStartMode(service["StartMode"] as string),
                                string.Equals(service["State"] as string, "Running", StringComparison.OrdinalIgnoreCase),
                                IsFromMicrosoft(name, service["PathName"] as string),
                                runningDependencies.Contains(name)));
                        }
                    }
                }
                catch (ManagementException)
                {
                    // WMI unavailable: an empty list, and the page says it found nothing rather
                    // than failing to open.
                    return services;
                }

                return [.. services.OrderBy(service => service.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
            },
            cancellationToken);

    /// <summary>
    /// Services that something currently running depends on. Turning one of those off stops the
    /// dependent too, which is why the classifier locks them whatever list they are on.
    /// </summary>
    private static HashSet<string> ReadRunningDependencies()
    {
        var antecedents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_DependentService");
            using var results = searcher.Get();

            foreach (ManagementObject association in results.Cast<ManagementObject>())
            {
                using (association)
                {
                    string? antecedent = ExtractServiceName(association["Antecedent"] as string);
                    if (antecedent is not null)
                    {
                        antecedents.Add(antecedent);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // Without the association data every service simply classifies on its own merits.
        }

        return antecedents;
    }

    /// <summary>WMI reports references as <c>Win32_Service.Name="Dnscache"</c>.</summary>
    private static string? ExtractServiceName(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        int start = reference.IndexOf('"', StringComparison.Ordinal);
        int end = reference.LastIndexOf('"');

        return end > start && start >= 0 ? reference[(start + 1)..end] : null;
    }

    /// <summary>
    /// Whether the binary behind a service comes from Microsoft. Used only to label the row, never
    /// to decide whether something is safe — a signature check would be the wrong tool for a
    /// question the curated list already answers by name.
    /// </summary>
    private bool IsFromMicrosoft(string serviceName, string? pathName)
    {
        string? binaryPath = ResolveBinaryPath(serviceName, pathName);
        if (binaryPath is null)
        {
            return false;
        }

        try
        {
            string? company = FileVersionInfo.GetVersionInfo(binaryPath).CompanyName;
            return company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// For a service hosted in svchost, the executable says nothing — the DLL named in the
    /// service's own Parameters key is the thing that actually runs.
    /// </summary>
    private string? ResolveBinaryPath(string serviceName, string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName))
        {
            return null;
        }

        string executable = ExtractExecutablePath(pathName);

        if (Path.GetFileNameWithoutExtension(executable).Equals("svchost", StringComparison.OrdinalIgnoreCase))
        {
            string? serviceDll = _registry.GetStringValue(
                RegistryHiveName.LocalMachine,
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}\Parameters",
                "ServiceDll");

            return string.IsNullOrWhiteSpace(serviceDll) ? executable : Environment.ExpandEnvironmentVariables(serviceDll);
        }

        return executable;
    }

    private static string ExtractExecutablePath(string pathName)
    {
        string trimmed = pathName.Trim();

        if (trimmed.StartsWith('"'))
        {
            int closing = trimmed.IndexOf('"', 1);
            return closing > 0 ? trimmed[1..closing] : trimmed.Trim('"');
        }

        // Unquoted paths with arguments: the executable ends at ".exe".
        int exe = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? trimmed[..(exe + 4)] : trimmed;
    }

    /// <summary>WMI's StartMode wording, mapped to what the UI shows.</summary>
    private static string NormalizeStartMode(string? startMode) => startMode switch
    {
        "Auto" => "Automatic",
        "Manual" => "Manual",
        "Disabled" => "Disabled",
        "Boot" or "System" => "Boot",
        _ => startMode ?? "Unknown",
    };
}
