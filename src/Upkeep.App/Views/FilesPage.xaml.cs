using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// Duplicates, large and forgotten files, and where the space went. Which tab is showing is view
/// state and stays here; everything about what the scans find, and what removing something means,
/// lives in the view model.
/// </summary>
public sealed partial class FilesPage : Page
{
    private readonly ILocalizationService _localization;

    public FilesPage()
    {
        ViewModel = App.Services.GetRequiredService<FilesViewModel>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        InitializeComponent();
    }

    public FilesViewModel ViewModel { get; }

    private string SelectedTab => (Tabs.SelectedItem?.Tag as string) ?? "Duplicates";

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        string tab = SelectedTab;

        DuplicatesPanel.Visibility = tab == "Duplicates" ? Visibility.Visible : Visibility.Collapsed;
        LargePanel.Visibility = tab == "Large" ? Visibility.Visible : Visibility.Collapsed;
        UsagePanel.Visibility = tab == "Usage" ? Visibility.Visible : Visibility.Collapsed;

        // Only duplicates and large files have anything to act on; the treemap is a view, not a
        // selection, so the action bar would be a dead control there.
        ActionBar.Visibility = tab == "Usage" ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            switch (SelectedTab)
            {
                case "Large":
                    await ViewModel.ScanLargeFilesAsync(CancellationToken.None);
                    break;
                case "Usage":
                    await ViewModel.ScanUsageAsync(CancellationToken.None);
                    break;
                default:
                    await ViewModel.ScanDuplicatesAsync(CancellationToken.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("A file scan failed to start.", ex);
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await ConfirmAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.RemoveSelectedAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Removing files failed to start.", ex);
        }
    }

    /// <summary>
    /// Permanent deletion gets its own confirmation wording. Moving to quarantine is recoverable
    /// for a week; deleting is not, and the dialog has to be honest about which one is about to
    /// happen.
    /// </summary>
    private async Task<ContentDialogResult> ConfirmAsync()
    {
        int count = ViewModel.SelectedPaths().Count();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localization.GetString(
                ViewModel.DeletePermanently ? "FilesConfirmDeleteTitleFormat" : "FilesConfirmQuarantineTitleFormat",
                count),
            Content = new TextBlock
            {
                Text = _localization.GetString(ViewModel.DeletePermanently ? "FilesConfirmDeleteBody" : "FilesConfirmQuarantineBody"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
            },
            PrimaryButtonText = _localization.GetString(ViewModel.DeletePermanently ? "FilesConfirmDeletePrimary" : "FilesConfirmQuarantinePrimary"),
            CloseButtonText = _localization.GetString("CleanupConfirmCancel"),
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources["PrimaryButtonStyle"],
        };

        return await dialog.ShowAsync();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder })
        {
            ViewModel.RemoveFolder(folder);
        }
    }
}
