using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// The preferences Upkeep keeps, all of them on this PC. Every control writes straight through —
/// there is no Apply button because there is nothing here worth staging.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    public SettingsViewModel ViewModel { get; }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The Settings page failed to load.", ex);
        }
    }

    private async void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true })
        {
            await SaveAsync();
        }
    }

    private async void Updates_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true })
        {
            await SaveAsync();
        }
    }

    private async void Retention_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // NumberBox reports NaN while the box is empty mid-edit; saving that would clamp to the
        // minimum behind the user's back.
        if (sender.IsLoaded && !double.IsNaN(args.NewValue))
        {
            await SaveAsync();
        }
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.CheckForUpdatesNowAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Checking for updates failed.", ex);
        }
    }

    private void EmptyQuarantine_Click(object sender, RoutedEventArgs e) => ViewModel.EmptyQuarantineNow();

    private async Task SaveAsync()
    {
        try
        {
            await ViewModel.SaveAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Saving the settings failed.", ex);
        }
    }
}
