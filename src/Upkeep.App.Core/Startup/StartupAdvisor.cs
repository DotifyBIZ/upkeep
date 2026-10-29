using Upkeep.App.Core.Performance;

namespace Upkeep.App.Core.Startup;

/// <summary>How long Windows measured a startup app taking, averaged over the starts it was logged in.</summary>
/// <param name="Average">Mean of the times Windows recorded.</param>
/// <param name="Samples">How many starts it was logged in.</param>
public sealed record StartupTiming(TimeSpan Average, int Samples);

/// <summary>One startup item with what Windows measured about it and what Upkeep makes of that.</summary>
/// <param name="Item">The startup item.</param>
/// <param name="Timing">What Windows measured; null when Windows never logged it as slowing a start.</param>
/// <param name="LastStartedByUser">When the person last started it themselves, if UserAssist saw it.</param>
/// <param name="Suggested">True when it is slow and the person hasn't started it in a while — pre-ticked in the preview, never acted on alone.</param>
public sealed record StartupInsight(StartupItem Item, StartupTiming? Timing, DateTimeOffset? LastStartedByUser, bool Suggested);

/// <summary>
/// Ranks startup items by the delay Windows measured, and picks the ones worth suggesting.
/// <para>
/// The suggestion is deliberately narrow: on, switchable, timed by Windows, and not started by the
/// person in <see cref="UnusedAfter"/>. An app Windows didn't time is never suggested — there is
/// nothing measured to say it costs anything (ADR-0010).
/// </para>
/// </summary>
public static class StartupAdvisor
{
    public static readonly TimeSpan UnusedAfter = TimeSpan.FromDays(30);

    /// <summary>
    /// Windows' own tray pieces. Turning them off hides a security or shell icon people rely on,
    /// which no startup saving is worth suggesting; they stay switchable by hand.
    /// </summary>
    private static readonly HashSet<string> NeverSuggested = new(StringComparer.OrdinalIgnoreCase)
    {
        "SecurityHealthSystray",
        "SecurityHealth",
    };

    public static IReadOnlyList<StartupInsight> Analyse(
        IReadOnlyList<StartupItem> items,
        BootPerformance? performance,
        ILaunchHistory history,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(history);

        var timings = TimingsByStem(performance);

        var insights = items.Select(item =>
        {
            var keys = LaunchHistoryMatch.KeysFor(item);
            StartupTiming? timing = StartupCommand.ExecutableStem(item.Command) is string stem
                && timings.TryGetValue(LaunchHistoryMatch.Normalize(stem), out var found)
                ? found
                : null;

            DateTimeOffset? lastStarted = history.LastStartedByUser(keys);

            bool suggested = item.IsEnabled
                && item.CanToggle
                && timing is not null
                && !keys.Any(NeverSuggested.Contains)
                && (lastStarted is null || now - lastStarted.Value > UnusedAfter);

            return new StartupInsight(item, timing, lastStarted, suggested);
        });

        // Slowest first; what Windows didn't time keeps the scanner's alphabetical order after them.
        return [.. insights.OrderByDescending(insight => insight.Timing?.Average ?? TimeSpan.MinValue)];
    }

    private static Dictionary<string, StartupTiming> TimingsByStem(BootPerformance? performance)
    {
        if (performance is null)
        {
            return [];
        }

        return performance.SlowApps
            .GroupBy(app => LaunchHistoryMatch.Normalize(Path.GetFileNameWithoutExtension(app.FileName)))
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => new StartupTiming(
                    TimeSpan.FromMilliseconds(group.Average(app => app.TotalTime.TotalMilliseconds)),
                    group.Count()));
    }
}
