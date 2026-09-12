using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// What starts with Windows. Each switch writes the same StartupApproved value Task Manager
/// writes, so nothing is deleted and History can put every change back.
/// </summary>
public sealed partial class StartupPage : Page
{
    public StartupPage()
    {
        ViewModel = App.Services.GetRequiredService<StartupViewModel>();
        InitializeComponent();
        Loaded += StartupPage_Loaded;
    }

    public StartupViewModel ViewModel { get; }

    private async void StartupPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The Startup page failed to load.", ex);
        }
    }

    /// <summary>
    /// Closes the session on the way out, so History shows one finished session for this visit
    /// rather than an open one that never completed.
    /// </summary>
    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        try
        {
            await ViewModel.EndSessionAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Closing the startup session failed.", ex);
        }
    }

    /// <summary>
    /// Writes a flipped switch through. Toggled also fires while the binding sets each row's
    /// initial state, so a switch that is not loaded yet is the binding talking, not the user.
    /// </summary>
    private async void OnStartupItemToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { IsLoaded: true, DataContext: StartupItemDisplay display })
        {
            return;
        }

        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.ApplyAsync(display, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync($"Changing the startup state of {display.Name} failed.", ex);
        }
    }
}
