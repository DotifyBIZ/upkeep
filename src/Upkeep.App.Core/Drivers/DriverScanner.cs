using System.Diagnostics.CodeAnalysis;
using System.Management;

namespace Upkeep.App.Core.Drivers;

/// <summary>Lists the signed drivers installed on this PC.</summary>
public interface IDriverScanner
{
    Task<IReadOnlyList<DriverInfo>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads drivers through WMI.
/// <para>
/// <c>Win32_PnPSignedDriver</c> rather than <c>pnputil</c>: pnputil prints in the user's display
/// language, and parsing localized tool output is exactly the mistake CLAUDE.md forbids — this
/// project is developed on Polish Windows, where it fails immediately. WMI property names are
/// stable everywhere (ADR-0009).
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads this machine's driver database through WMI; which drivers are worth showing is decided in DriverCatalog, which is tested.")]
public sealed class DriverScanner : IDriverScanner
{
    private const string Query =
        "SELECT DeviceName, Manufacturer, DriverVersion, DriverDate, DeviceID, DeviceClass FROM Win32_PnPSignedDriver";

    public Task<IReadOnlyList<DriverInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DriverInfo>>(
            () =>
            {
                var drivers = new List<DriverInfo>();

                try
                {
                    using var searcher = new ManagementObjectSearcher(Query);
                    using var results = searcher.Get();

                    foreach (ManagementObject driver in results.Cast<ManagementObject>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        using (driver)
                        {
                            drivers.Add(new DriverInfo(
                                driver["DeviceName"] as string ?? string.Empty,
                                driver["Manufacturer"] as string,
                                driver["DriverVersion"] as string ?? string.Empty,
                                ParseDate(driver["DriverDate"] as string),
                                driver["DeviceID"] as string,
                                driver["DeviceClass"] as string));
                        }
                    }
                }
                catch (ManagementException)
                {
                    // A machine whose WMI driver provider won't answer shows an empty list rather
                    // than a failed page.
                    return [];
                }

                return DriverCatalog.Worth(drivers);
            },
            cancellationToken);

    /// <summary>
    /// WMI dates arrive as CIM_DATETIME ("20240115000000.000000+000"), not as anything
    /// <see cref="DateTime.Parse(string)"/> would understand.
    /// </summary>
    private static DateTimeOffset? ParseDate(string? cimDateTime)
    {
        if (string.IsNullOrWhiteSpace(cimDateTime))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(cimDateTime);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException)
        {
            return null;
        }
    }
}
