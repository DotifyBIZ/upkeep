using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// Drivers and Windows Update. Upkeep finds driver updates and hands the install to Windows, which
/// already knows how to finish after a restart (ADR-0009).
/// </summary>
public sealed partial class DriversPage : Page
{
    public DriversPage()
    {
        ViewModel = App.Services.GetRequiredService<DriversViewModel>();
        InitializeComponent();
        Loaded += DriversPage_Loaded;
    }

    public DriversViewModel ViewModel { get; }

    private async void DriversPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);

            // Home ignores the deferral policies, so the card is not shown there at all.
            DeferralCard.Visibility = ViewModel.SupportsDeferral ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The Drivers page failed to load.", ex);
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
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Closing the drivers session failed.", ex);
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool drivers = (Tabs.SelectedItem?.Tag as string) != "Updates";

        DriversPanel.Visibility = drivers ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPanel.Visibility = drivers ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ViewModel.CheckForUpdatesAsync(CancellationToken.None), "Searching for driver updates failed.");

    private async void Pause_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ViewModel.PauseUpdatesAsync(CancellationToken.None), "Pausing Windows Update failed.");

    private async void Resume_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ViewModel.ResumeUpdatesAsync(CancellationToken.None), "Resuming Windows Update failed.");

    private async void ApplyDeferral_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ViewModel.ApplyDeferralAsync(CancellationToken.None), "Changing the update deferral failed.");

    private static async Task RunAsync(Func<Task> action, string failureMessage)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync(failureMessage, ex);
        }
    }
}
