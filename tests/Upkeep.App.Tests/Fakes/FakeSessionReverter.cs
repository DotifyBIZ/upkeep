using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Tests.Fakes;

/// <summary>
/// Stands in for the real reverter, reporting whatever outcome the test needs.
/// <para>
/// It stamps <see cref="SessionManifest.RevertedAt"/> through the journal like the real one does,
/// so a test that checks History stops offering a reverted session is testing something.
/// </para>
/// </summary>
public sealed class FakeSessionReverter : ISessionReverter
{
    private readonly ISessionJournal _journal;

    public FakeSessionReverter(ISessionJournal journal) => _journal = journal;

    public List<string> RevertedSessionIds { get; } = [];

    public RevertOutcome Outcome { get; set; } = new(RevertedCount: 1, FailedCount: 0, SkippedCount: 0);

    public async Task<(SessionManifest Session, RevertOutcome Outcome)> RevertAsync(
        SessionManifest session,
        CancellationToken cancellationToken = default)
    {
        RevertedSessionIds.Add(session.Id);

        var stamped = await _journal.SaveAsync(session with { RevertedAt = DateTimeOffset.UtcNow }, cancellationToken);
        return (stamped, Outcome);
    }
}
