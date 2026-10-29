using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace Upkeep.App.Core.Performance;

/// <summary>One start of Windows, as Windows itself timed it.</summary>
/// <param name="At">When Windows logged it.</param>
/// <param name="Duration">From Windows starting to load until the desktop had settled.</param>
public sealed record BootRecord(DateTimeOffset At, TimeSpan Duration);

/// <summary>An app Windows says made one start slower than usual.</summary>
/// <param name="At">When Windows logged it.</param>
/// <param name="FileName">The executable's file name, for example <c>OneDrive.exe</c>.</param>
/// <param name="FriendlyName">The name in the executable's version resource, where it has one.</param>
/// <param name="TotalTime">How long the app took to start.</param>
/// <param name="DegradationTime">How much longer than usual that was.</param>
public sealed record SlowStartupApp(DateTimeOffset At, string FileName, string? FriendlyName, TimeSpan TotalTime, TimeSpan DegradationTime);

/// <summary>What the Diagnostics-Performance log says about recent starts, newest first.</summary>
public sealed record BootPerformance(IReadOnlyList<BootRecord> Boots, IReadOnlyList<SlowStartupApp> SlowApps)
{
    public static BootPerformance Empty { get; } = new([], []);
}

/// <summary>
/// Turns Diagnostics-Performance events into records (ADR-0010).
/// <para>
/// Values are taken by position from the event's properties, never from its message — the message
/// is localized, and on Polish Windows it doesn't contain one English word to anchor on. The
/// positions come from the provider's own metadata.
/// </para>
/// </summary>
public static class BootPerformanceParser
{
    /// <summary>"Windows has started up" — one per start.</summary>
    public const int BootEventId = 100;

    /// <summary>"This application took longer than usual to start up".</summary>
    public const int SlowAppEventId = 101;

    // Zero-based property indexes; the provider's messages number them from one (%6, %3, ...).
    private const int BootTimeIndex = 5;
    private const int AppFileNameIndex = 2;
    private const int AppFriendlyNameIndex = 4;
    private const int AppTotalTimeIndex = 7;
    private const int AppDegradationTimeIndex = 8;

    /// <summary>Anything past this is a broken record rather than a slow PC.</summary>
    private static readonly TimeSpan Implausible = TimeSpan.FromHours(1);

    public static BootRecord? ParseBoot(DateTimeOffset at, IReadOnlyList<object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return TryMilliseconds(properties, BootTimeIndex, out TimeSpan duration) && duration > TimeSpan.Zero
            ? new BootRecord(at, duration)
            : null;
    }

    public static SlowStartupApp? ParseSlowApp(DateTimeOffset at, IReadOnlyList<object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (properties.Count <= AppDegradationTimeIndex
            || properties[AppFileNameIndex] is not string fileName
            || string.IsNullOrWhiteSpace(fileName)
            || !TryMilliseconds(properties, AppTotalTimeIndex, out TimeSpan total)
            || !TryMilliseconds(properties, AppDegradationTimeIndex, out TimeSpan degradation))
        {
            return null;
        }

        string? friendlyName = properties[AppFriendlyNameIndex] as string;
        return new SlowStartupApp(
            at,
            Path.GetFileName(fileName.Trim()),
            string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName.Trim(),
            total,
            degradation);
    }

    private static bool TryMilliseconds(IReadOnlyList<object?> properties, int index, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (index >= properties.Count)
        {
            return false;
        }

        long milliseconds;
        switch (properties[index])
        {
            case uint unsigned:
                milliseconds = unsigned;
                break;
            case ulong unsignedLong when unsignedLong <= long.MaxValue:
                milliseconds = (long)unsignedLong;
                break;
            case int signed:
                milliseconds = signed;
                break;
            case long signedLong:
                milliseconds = signedLong;
                break;
            case string text when long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed):
                milliseconds = parsed;
                break;
            default:
                return false;
        }

        if (milliseconds < 0 || milliseconds > Implausible.TotalMilliseconds)
        {
            return false;
        }

        value = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}

/// <summary>Reads recent starts from Windows' Diagnostics-Performance log. Administrator-only, so
/// only the elevated helper calls it.</summary>
public interface IBootPerformanceLog
{
    BootPerformance Read(int maxBoots, CancellationToken cancellationToken);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin EventLogReader wrapper; turning its records into values is BootPerformanceParser, which is tested.")]
public sealed class BootPerformanceLog : IBootPerformanceLog
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    public BootPerformance Read(int maxBoots, CancellationToken cancellationToken)
    {
        var boots = new List<BootRecord>();
        var apps = new List<SlowStartupApp>();

        var query = new EventLogQuery(
            LogName,
            PathType.LogName,
            $"*[System[(EventID={BootPerformanceParser.BootEventId} or EventID={BootPerformanceParser.SlowAppEventId})]]")
        {
            ReverseDirection = true,
        };

        try
        {
            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (record.TimeCreated is not DateTime created)
                    {
                        continue;
                    }

                    var at = new DateTimeOffset(created.ToUniversalTime(), TimeSpan.Zero);
                    var values = record.Properties.Select(property => property.Value).ToList();

                    if (record.Id == BootPerformanceParser.BootEventId)
                    {
                        if (boots.Count == maxBoots)
                        {
                            // Newest first, so the first boot past the cap is older than anything
                            // Speed check will show, and so is every app event after it.
                            break;
                        }

                        if (BootPerformanceParser.ParseBoot(at, values) is BootRecord boot)
                        {
                            boots.Add(boot);
                        }
                    }
                    else if (BootPerformanceParser.ParseSlowApp(at, values) is SlowStartupApp app)
                    {
                        apps.Add(app);
                    }
                }
            }
        }
        catch (EventLogNotFoundException)
        {
            // Windows editions without the channel have nothing to say, which is not an error.
            return BootPerformance.Empty;
        }

        return new BootPerformance(boots, apps);
    }
}
