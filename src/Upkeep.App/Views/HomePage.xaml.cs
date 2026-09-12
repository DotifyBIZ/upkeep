using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
        Loaded += HomePage_Loaded;
    }

    public HomeViewModel ViewModel { get; }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable for a XAML event handler, so nothing may escape it: an
        // unhandled exception here would take the process down rather than fail a page load.
        try
        {
            await ViewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<Upkeep.App.Core.Logging.IAppLogger>()
                .LogErrorAsync("Home failed to load.", ex);
        }
    }

    private void ScanThisPc_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(CleanupPage));
}
