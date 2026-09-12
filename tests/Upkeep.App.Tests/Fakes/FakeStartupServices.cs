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
}
