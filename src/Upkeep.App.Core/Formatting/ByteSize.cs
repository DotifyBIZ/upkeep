using System.Globalization;

namespace Upkeep.App.Core.Formatting;

/// <summary>
/// Turns byte counts into the short, rounded sizes the UI shows ("812 MB", "1.9 GB"). Binary
/// units with decimal-style labels, matching what File Explorer reports for the same file — a
/// cleaner that disagrees with Explorer about how big something is invites exactly the wrong kind
/// of doubt.
/// </summary>
public static class ByteSize
{
    private const long Kilobyte = 1024;
    private const long Megabyte = Kilobyte * 1024;
    private const long Gigabyte = Megabyte * 1024;
    private const long Terabyte = Gigabyte * 1024;

    /// <summary>
    /// Formats <paramref name="bytes"/> for display in the current culture (Polish uses a comma
    /// as the decimal separator, so this is never string-concatenated by hand).
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return Format(0);
        }

        var culture = CultureInfo.CurrentCulture;

        return bytes switch
        {
            < Kilobyte => string.Create(culture, $"{bytes} B"),
            < Megabyte => string.Create(culture, $"{bytes / (double)Kilobyte:0} KB"),
            // Under 10 units, one decimal is the difference between "1 GB" and "1.9 GB"; above it,
            // the decimal is noise.
            < Gigabyte => string.Create(culture, $"{bytes / (double)Megabyte:0} MB"),
            < 10 * Gigabyte => string.Create(culture, $"{bytes / (double)Gigabyte:0.0} GB"),
            < Terabyte => string.Create(culture, $"{bytes / (double)Gigabyte:0} GB"),
            _ => string.Create(culture, $"{bytes / (double)Terabyte:0.00} TB"),
        };
    }
}
