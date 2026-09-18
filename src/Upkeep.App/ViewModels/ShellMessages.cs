namespace Upkeep.App.ViewModels;

/// <summary>
/// Asks the shell to play the welcome tour. Sent by Settings, handled by MainPage, which is where
/// the dialog lives — a page inside the frame has no business owning a window-level modal.
/// </summary>
public sealed record ShowWelcomeMessage;

/// <summary>
/// A cleanup run finished. The shell shows this as a toast only when the user has navigated away
/// from the Cleanup page — the results are already on screen for anyone who stayed.
/// </summary>
/// <param name="FreedSummary">What the results screen says it freed, already formatted.</param>
public sealed record CleanupFinishedMessage(string FreedSummary);
