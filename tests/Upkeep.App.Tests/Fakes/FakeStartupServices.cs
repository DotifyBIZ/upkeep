using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.Tests.Fakes;

/// <summary>Returns whatever startup list a test sets up, without reading the real registry.</summary>
public sealed class FakeStartupItemScanner : IStartupItemScanner
{
    public List<StartupItem> Items { get; } = [];

    public Task<IReadOnlyList<StartupItem>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StartupItem>>(Items);
}

/// <summary>
/// Records what the view model asked to change, and can refuse — the case that matters, because a
/// switch must never sit in a state Windows didn't accept.
/// </summary>
public sealed class FakeStartupItemToggler : IStartupItemToggler
{
    public bool Succeeds { get; set; } = true;

    public List<(string ItemId, bool Enabled)> Changes { get; } = [];

    public Task<(SessionManifest Session, bool Success)> SetEnabledAsync(
        SessionManifest session,
        StartupItem item,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        Changes.Add((item.Id, enabled));
        return Task.FromResult((session, Succeeds));
    }

    public Task<(bool Success, string? Failure)> SetEnabledWithoutJournalAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default)
    {
        Changes.Add((item.Id, enabled));
        string? failure = Succeeds ? null : "Windows refused it.";
        return Task.FromResult((Succeeds, failure));
    }
}

/// <summary>Hands out whatever startup timings the test set, and remembers whether it was asked to prompt.</summary>
public sealed class FakeBootPerformanceSource : IBootPerformanceSource
{
    /// <summary>What a read would find. Null means the read fails with <see cref="Status"/>.</summary>
    public BootPerformance? Available { get; set; }

    /// <summary>What a read without approval answers when there is nothing cached yet.</summary>
    public BootPerformanceStatus Status { get; set; } = BootPerformanceStatus.NeedsApproval;

    /// <summary>True when the helper is already running, so a read needs no prompt.</summary>
    public bool AlreadyElevated { get; set; }

    public List<bool> Reads { get; } = [];

    public BootPerformance? Cached { get; private set; }

    public Task<BootPerformanceStatus> ReadAsync(bool allowPrompt, CancellationToken cancellationToken = default)
    {
        Reads.Add(allowPrompt);

        if (Cached is null && Available is not null && (allowPrompt || AlreadyElevated))
        {
            Cached = Available;
        }

        return Task.FromResult(Cached is not null ? BootPerformanceStatus.Read : Status);
    }
}

/// <summary>Answers "last started by you" from a table, by normalized key.</summary>
public sealed class FakeLaunchHistory : ILaunchHistory
{
    public Dictionary<string, DateTimeOffset> Launches { get; } = new(StringComparer.Ordinal);

    public DateTimeOffset? LastStartedByUser(IEnumerable<string> keys)
    {
        var hits = keys.Select(LaunchHistoryMatch.Normalize).Where(Launches.ContainsKey).Select(key => Launches[key]).ToList();
        return hits.Count == 0 ? null : hits.Max();
    }
}
