namespace Upkeep.App.Core.Drivers;

/// <summary>
/// One signed driver as Windows reports it.
/// </summary>
/// <param name="DeviceName">What Device Manager calls the device.</param>
/// <param name="Manufacturer">Who signed the driver, where Windows recorded one.</param>
/// <param name="Version">The installed driver version, e.g. "6.0.9285.1".</param>
/// <param name="Date">When the driver was built, where Windows recorded one.</param>
/// <param name="DeviceId">The PnP device id — what Device Manager is opened against for a rollback.</param>
/// <param name="DeviceClass">The class Windows files it under (Display, Net, Media).</param>
public sealed record DriverInfo(
    string DeviceName,
    string? Manufacturer,
    string Version,
    DateTimeOffset? Date,
    string? DeviceId,
    string? DeviceClass);

/// <summary>
/// Decides which drivers are worth putting in front of someone.
/// <para>
/// Pure, and separate from the WMI reader, because this is the part with judgement in it:
/// <c>Win32_PnPSignedDriver</c> returns a row per device, so one driver package shared by three
/// ports appears three times, and plenty of rows describe things nobody would call a device.
/// </para>
/// </summary>
public static class DriverCatalog
{
    /// <summary>
    /// Trims a raw driver list down to what the page shows: named devices with a version, one row
    /// per driver rather than per device, ordered the way someone would look for them.
    /// </summary>
    public static IReadOnlyList<DriverInfo> Worth(IEnumerable<DriverInfo> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        return [.. drivers
            .Where(driver => !string.IsNullOrWhiteSpace(driver.DeviceName) && !string.IsNullOrWhiteSpace(driver.Version))
            .DistinctBy(driver => (driver.DeviceName, driver.Version), NameAndVersionComparer.Instance)
            .OrderBy(driver => driver.DeviceName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Whether Windows Update is offering something newer for this driver.
    /// <para>
    /// Matched on the model the update declares, and otherwise on the device name appearing in the
    /// update title — which is how these updates are actually named. Deliberately conservative:
    /// claiming an update exists for the wrong device is worse than not spotting one.
    /// </para>
    /// </summary>
    public static bool HasUpdate(DriverInfo driver, IEnumerable<DriverUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(updates);

        if (string.IsNullOrWhiteSpace(driver.DeviceName))
        {
            return false;
        }

        return updates.Any(update =>
            string.Equals(update.DriverModel, driver.DeviceName, StringComparison.OrdinalIgnoreCase)
            || update.Title.Contains(driver.DeviceName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Device names and versions are compared the way Windows treats them — case-insensitively.
    /// A tuple's default comparer would call "Realtek Audio" and "REALTEK AUDIO" two devices.
    /// </summary>
    private sealed class NameAndVersionComparer : IEqualityComparer<(string DeviceName, string Version)>
    {
        public static readonly NameAndVersionComparer Instance = new();

        public bool Equals((string DeviceName, string Version) left, (string DeviceName, string Version) right) =>
            string.Equals(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string DeviceName, string Version) value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.DeviceName),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version));
    }
}
