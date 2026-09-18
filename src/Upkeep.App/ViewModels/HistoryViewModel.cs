using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.ViewModels;

/// <summary>
/// One past session as History shows it: what kind of work it was, when, what it changed, and
/// whether any of it can still be put back.
/// </summary>
public sealed class HistorySessionDisplay
{
    public HistorySessionDisplay(
        SessionManifest session,
        string kindLabel,
        string whenDisplay,
        string changesDisplay,
        string? freedDisplay,
        string? restorePointNote,
        string? unfinishedNote,
        string? revertedNote)
    {
        Session = session;
        KindLabel = kindLabel;
        WhenDisplay = whenDisplay;
        ChangesDisplay = changesDisplay;
        FreedDisplay = freedDisplay;
        RestorePointNote = restorePointNote;
        UnfinishedNote = unfinishedNote;
        RevertedNote = revertedNote;
    }

    public SessionManifest Session { get; }

    public string Id => Session.Id;

    /// <summary>Which part of Upkeep made the change, localized.</summary>
    public string KindLabel { get; }

    public string WhenDisplay { get; }

    public string ChangesDisplay { get; }

    /// <summary>Null when the session freed nothing, so the row doesn't say "0 B freed".</summary>
    public string? FreedDisplay { get; }

    public bool HasFreedDisplay => !string.IsNullOrEmpty(FreedDisplay);

    /// <summary>Whether a restore point covers this session, and whether it was made or reused.</summary>
    public string? RestorePointNote { get; }

    public bool HasRestorePointNote => !string.IsNullOrEmpty(RestorePointNote);

    /// <summary>Set when the session never completed — an interrupted run, shown rather than hidden.</summary>
    public string? UnfinishedNote { get; }

    public bool HasUnfinishedNote => !string.IsNullOrEmpty(UnfinishedNote);

    /// <summary>Set once the session has been reverted, so it isn't offered again.</summary>
    public string? RevertedNote { get; }

    public bool HasRevertedNote => !string.IsNullOrEmpty(RevertedNote);

    /// <summary>True while anything in the session can still be put back.</summary>
    public bool CanRevert => Session.CanRevert;
}

/// <summary>
/// The History tab: every session Upkeep recorded, newest first, with one button to put a whole
/// session back (ADR-0006).
/// <para>
/// A session that contains nothing reversible still appears — History is the record of what was
/// done, not only of what can be undone, and saying plainly that something can't be undone is part
/// of the promise.
/// </para>
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ISessionJournal _journal;
    private readonly ISessionReverter _reverter;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    public HistoryViewModel(
        ISessionJournal journal,
        ISessionReverter reverter,
        ILocalizationService localization,
        IAppLogger logger)
    {
        _journal = journal;
        _reverter = reverter;
        _localization = localization;
        _logger = logger;
    }

    public ObservableCollection<HistorySessionDisplay> Sessions { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;

        try
        {
            // The journal already returns these newest first; re-sorting here would be a second
            // opinion about the same thing.
            var sessions = await _journal.ListAsync(cancellationToken);

            Sessions.Clear();
            foreach (var session in sessions)
            {
                Sessions.Add(BuildDisplay(session, _localization));
            }

            IsEmpty = Sessions.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading the session history failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Puts one session back. The result is reported as what actually happened rather than as a
    /// blanket success: a session can be partly reversible, and saying otherwise would be a lie
    /// the user finds out about later.
    /// </summary>
    public async Task RevertAsync(HistorySessionDisplay display, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (!display.CanRevert)
        {
            StatusMessage = _localization.GetString("HistoryNothingToRevert");
            return;
        }

        try
        {
            (_, var outcome) = await _reverter.RevertAsync(display.Session, cancellationToken);

            string result = outcome.AnythingReverted
                ? _localization.GetString(
                    "HistoryRevertResultFormat",
                    outcome.RevertedCount,
                    outcome.RevertedCount + outcome.FailedCount)
                : _localization.GetString("HistoryNothingToRevert");

            // What failed is added to what worked rather than replacing it: a partly reverted
            // session is both of those things, and hiding either half misleads.
            StatusMessage = outcome.FailedCount > 0
                ? string.Concat(result, " ", _localization.GetString("HistoryRevertFailedFormat", outcome.FailedCount))
                : result;

            // The session now carries RevertedAt, so the list has to be rebuilt for the button to
            // stop offering what has already been done.
            await LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Reverting session {display.Id} failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
    }

    /// <summary>
    /// Turns a raw session into the words History (and Home's recent-activity list, which shares
    /// this rather than inventing its own phrasing for the same session) shows for it.
    /// </summary>
    internal static HistorySessionDisplay BuildDisplay(SessionManifest session, ILocalizationService localization)
    {
        string? freed = session.FreedBytes > 0
            ? localization.GetString("HistoryFreedFormat", ByteSize.Format(session.FreedBytes))
            : null;

        string? restorePoint = session.RestorePointDescription is null
            ? null
            : localization.GetString(session.RestorePointWasReused ? "HistoryRestorePointReused" : "HistoryRestorePointCreated");

        return new HistorySessionDisplay(
            session,
            localization.GetString($"HistoryKind{session.Kind}"),
            session.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            localization.GetString("HistoryChangesFormat", session.CompletedCount),
            freed,
            restorePoint,
            session.CompletedAt is null ? localization.GetString("HistoryUnfinished") : null,
            session.RevertedAt is DateTimeOffset reverted
                ? localization.GetString("HistoryRevertedFormat", reverted.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : null);
    }
}
