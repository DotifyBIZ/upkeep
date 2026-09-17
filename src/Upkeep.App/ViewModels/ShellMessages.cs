namespace Upkeep.App.ViewModels;

/// <summary>
/// Asks the shell to play the welcome tour. Sent by Settings, handled by MainPage, which is where
/// the dialog lives — a page inside the frame has no business owning a window-level modal.
/// </summary>
public sealed record ShowWelcomeMessage;
