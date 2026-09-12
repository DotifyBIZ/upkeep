using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// Everything Upkeep has done on this PC, newest first, with one button to put a session back.
/// </summary>
public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        InitializeComponent();
        Loaded += HistoryPage_Loaded;
    }

    public HistoryViewModel ViewModel { get; }

    private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The History page failed to load.", ex);
        }
    }

    private async void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HistorySessionDisplay display })
        {
            return;
        }

        try
        {
            await ViewModel.RevertAsync(display, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync($"Reverting session {display.Id} failed.", ex);
        }
    }
}
