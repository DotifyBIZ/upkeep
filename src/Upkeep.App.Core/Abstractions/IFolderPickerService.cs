namespace Upkeep.App.Core.Abstractions;

/// <summary>
/// Asks the user to pick a folder. A shell capability rather than a Core one — the picker needs a
/// window to belong to — so it lives behind this interface and is implemented in the WinUI layer.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>The chosen folder's full path, or null if the user closed the picker.</summary>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
