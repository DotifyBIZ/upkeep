using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// Startup and performance: what runs when Windows starts, what runs in the background, and the
/// handful of Windows settings worth a switch.
/// <para>
/// Three tabs over three view models rather than one, because they share nothing but the page:
/// each turns its own changes into its own journal entries, and each closes its own session.
/// </para>
/// </summary>
public sealed partial class StartupPage : Page
{
    public StartupPage()
    {
        ViewModel = App.Services.GetRequiredService<StartupViewModel>();
        Services = App.Services.GetRequiredService<ServicesViewModel>();
        Performance = App.Services.GetRequiredService<PerformanceViewModel>();

        InitializeComponent();
        Loaded += StartupPage_Loaded;
    }

    public StartupViewModel ViewModel { get; }

    public ServicesViewModel Services { get; }

    public PerformanceViewModel Performance { get; }

    /// <summary>The banner shows whichever tab is in front, so a warning can't follow you around.</summary>
    public string? CurrentStatusMessage => SelectedTab switch
    {
        "Services" => Services.StatusMessage,
        "Performance" => Performance.StatusMessage,
        _ => ViewModel.StatusMessage,
    };

    public bool CurrentStatusIsOpen => !string.IsNullOrEmpty(CurrentStatusMessage);

    private string SelectedTab => (Tabs.SelectedItem?.Tag as string) ?? "Startup";

    private async void StartupPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
            await Services.LoadAsync(CancellationToken.None);
            await Performance.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The Startup page failed to load.", ex);
        }
    }

    /// <summary>
    /// Closes every session on the way out, so History shows one finished session per tab that was
    /// actually used rather than open ones that never completed.
    /// </summary>
    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        try
        {
            await ViewModel.EndSessionAsync(CancellationToken.None);
            await Services.EndSessionAsync(CancellationToken.None);
            await Performance.EndSessionAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Closing the startup sessions failed.", ex);
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        string tab = SelectedTab;

        StartupPanel.Visibility = tab == "Startup" ? Visibility.Visible : Visibility.Collapsed;
        ServicesPanel.Visibility = tab == "Services" ? Visibility.Visible : Visibility.Collapsed;
        PerformancePanel.Visibility = tab == "Performance" ? Visibility.Visible : Visibility.Collapsed;

        // Each tab's footnote says something only true of that tab.
        StartupNote.Visibility = tab == "Startup" ? Visibility.Visible : Visibility.Collapsed;
        ServicesNote.Visibility = tab == "Services" ? Visibility.Visible : Visibility.Collapsed;
        ServicesSpeedNote.Visibility = ServicesNote.Visibility;

        RefreshStatus();
    }

    private void ServiceFilters_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        Services.SelectedFilter = (ServiceFilters.SelectedItem?.Tag as string) switch
        {
            "ThirdParty" => ServiceFilter.ThirdParty,
            "All" => ServiceFilter.All,
            _ => ServiceFilter.CommonlySafe,
        };
    }

    private async void OnStartupItemToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { IsLoaded: true, DataContext: StartupItemDisplay display })
        {
            return;
        }

        await RunAsync(() => ViewModel.ApplyAsync(display, CancellationToken.None), $"Changing the startup state of {display.Name} failed.");
    }

    private async void OnServiceToggled(object sender, RoutedEventArgs e)
    {
        // Toggled also fires while the binding sets each row's initial state, so a switch that is
        // not loaded yet is the binding talking, not the user.
        if (sender is not ToggleSwitch { IsLoaded: true, DataContext: ServiceItemDisplay display })
        {
            return;
        }

        await RunAsync(() => Services.ApplyAsync(display, CancellationToken.None), $"Changing the start type of {display.Name} failed.");
    }

    private async void OnAnimationsToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true })
        {
            await RunAsync(() => Performance.ApplyAnimationsAsync(CancellationToken.None), "Changing the animation setting failed.");
        }
    }

    private async void OnTransparencyToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true })
        {
            await RunAsync(() => Performance.ApplyTransparencyAsync(CancellationToken.None), "Changing the transparency setting failed.");
        }
    }

    private async void OnIndexingToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true })
        {
            await RunAsync(() => Performance.ApplyIndexingAsync(CancellationToken.None), "Changing search indexing failed.");
        }
    }

    private async void OnPowerPlanChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true })
        {
            await RunAsync(() => Performance.ApplyPowerPlanAsync(CancellationToken.None), "Changing the power plan failed.");
        }
    }

    /// <summary>
    /// Runs one view-model call from a XAML handler. async void is unavoidable there, so this is
    /// the one place an exception can escape — and it doesn't.
    /// </summary>
    private async Task RunAsync(Func<Task> action, string failureMessage)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync(failureMessage, ex);
        }
        finally
        {
            RefreshStatus();
        }
    }

    private void RefreshStatus()
    {
        Bindings.Update();
    }
}
