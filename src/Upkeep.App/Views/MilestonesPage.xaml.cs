using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// How regularly this PC is looked after, and the milestones that adds up to. Its own section for
/// now; it may move onto Home once it has been tried.
/// </summary>
public sealed partial class MilestonesPage : Page
{
    public MilestonesPage()
    {
        ViewModel = App.Services.GetRequiredService<MilestonesViewModel>();
        InitializeComponent();
        Loaded += MilestonesPage_Loaded;
    }

    public MilestonesViewModel ViewModel { get; }

    private async void MilestonesPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The Milestones page failed to load.", ex);
        }
    }
}
