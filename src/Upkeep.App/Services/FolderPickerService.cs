using Upkeep.App.Core.Abstractions;
using Windows.Storage.Pickers;

namespace Upkeep.App.Services;

/// <summary>
/// Wraps Windows' folder picker. Unpackaged apps have to tell the picker which window it belongs
/// to — without <c>InitializeWithWindow</c> it throws rather than opening, which is one of the
/// standard unpackaged-WinUI surprises.
/// </summary>
public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken);
        return folder?.Path;
    }
}
