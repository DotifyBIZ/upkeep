using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Performance;
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
        SpeedCheck = App.Services.GetRequiredService<SpeedCheckViewModel>();
        ViewModel = App.Services.GetRequiredService<StartupViewModel>();
        Services = App.Services.GetRequiredService<ServicesViewModel>();
        Performance = App.Services.GetRequiredService<PerformanceViewModel>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();

        InitializeComponent();
        Loaded += StartupPage_Loaded;
    }

    private readonly ILocalizationService _localization;

    public SpeedCheckViewModel SpeedCheck { get; }

    public StartupViewModel ViewModel { get; }

    public ServicesViewModel Services { get; }

    public PerformanceViewModel Performance { get; }

    /// <summary>The banner shows whichever tab is in front, so a warning can't follow you around.</summary>
    public string? CurrentStatusMessage => SelectedTab switch
    {
        "SpeedCheck" => SpeedCheck.StatusMessage,
        "Services" => Services.StatusMessage,
        "Performance" => Performance.StatusMessage,
        _ => ViewModel.StatusMessage,
    };

    public bool CurrentStatusIsOpen => !string.IsNullOrEmpty(CurrentStatusMessage);

    private string SelectedTab => (Tabs.SelectedItem?.Tag as string) ?? "SpeedCheck";

    private async void StartupPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await SpeedCheck.LoadAsync(CancellationToken.None);
            await ViewModel.LoadAsync(CancellationToken.None);
            await Services.LoadAsync(CancellationToken.None);
            await Performance.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The Startup page failed to load.", ex);
        }
        finally
        {
            // A load that failed put its reason in StatusMessage, and the banner is refreshed by
            // hand rather than by notification. Without this, the one message worth reading is the
            // one message never shown.
            RefreshStatus();
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

    private async void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        string tab = SelectedTab;

        SpeedCheckPanel.Visibility = tab == "SpeedCheck" ? Visibility.Visible : Visibility.Collapsed;
        StartupPanel.Visibility = tab == "Startup" ? Visibility.Visible : Visibility.Collapsed;
        ServicesPanel.Visibility = tab == "Services" ? Visibility.Visible : Visibility.Collapsed;
        PerformancePanel.Visibility = tab == "Performance" ? Visibility.Visible : Visibility.Collapsed;

        // Each tab's footnote says something only true of that tab.
        StartupNote.Visibility = tab == "Startup" ? Visibility.Visible : Visibility.Collapsed;
        ServicesNote.Visibility = tab == "Services" ? Visibility.Visible : Visibility.Collapsed;
        ServicesSpeedNote.Visibility = ServicesNote.Visibility;

        RefreshStatus();

        // What would help changes as the other tabs are used, so coming back re-reads it.
        if (tab == "SpeedCheck" && IsLoaded)
        {
            await RunAsync(() => SpeedCheck.LoadAsync(CancellationToken.None), "Refreshing Speed check failed.");
        }
    }

    private async void OnSpeedCheckMeasureClicked(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            await SpeedCheck.MeasureAsync(CancellationToken.None);

            // One read serves both tabs; the Startup tab picks the timings up without asking again.
            await ViewModel.LoadAsync(CancellationToken.None);
        }, "Measuring startup failed.");
    }

    private async void OnStartupMeasureClicked(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => ViewModel.MeasureAsync(CancellationToken.None), "Measuring startup failed.");
    }

    private void OnRecommendationClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target })
        {
            return;
        }

        foreach (var item in Tabs.Items)
        {
            item.IsSelected = (item.Tag as string) == target;
        }
    }

    /// <summary>
    /// Shows exactly which startup apps would be turned off, each one untickable, before anything
    /// changes. The preview is the view model's; this only draws it.
    /// </summary>
    private async void OnPreviewPlanClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.PreparePlan();
        if (ViewModel.PlanItems.Count == 0)
        {
            return;
        }

        var lines = new StackPanel { Spacing = 8 };
        lines.Children.Add(new TextBlock
        {
            Text = _localization.GetString("StartupPlanIntro"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var line in ViewModel.PlanItems)
        {
            var box = new CheckBox { IsChecked = line.IsSelected };
            var label = new StackPanel { Spacing = 2 };
            label.Children.Add(new TextBlock { Text = line.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = line.Detail, Style = (Style)Application.Current.Resources["CaptionTextStyle"] });
            box.Content = label;
            box.Checked += (_, _) => line.IsSelected = true;
            box.Unchecked += (_, _) => line.IsSelected = false;
            lines.Children.Add(box);
        }

        lines.Children.Add(new TextBlock
        {
            Text = _localization.GetString("StartupPlanMeasureNote"),
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localization.GetString("StartupPlanTitle"),
            Content = new ScrollViewer { Content = lines, MaxHeight = 420 },
            PrimaryButtonText = _localization.GetString("StartupPlanPrimary"),
            CloseButtonText = _localization.GetString("StartupPlanCancel"),
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonStyle = (Style)Application.Current.Resources["PrimaryButtonStyle"],
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunAsync(() => ViewModel.ApplyPlanAsync(CancellationToken.None), "Turning off the suggested startup apps failed.");
        }
    }

    private async void OnPluggedInModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true })
        {
            await RunAsync(() => Performance.ApplyPowerModeAsync(PowerSource.PluggedIn, CancellationToken.None), "Changing the power mode failed.");
        }
    }

    private async void OnOnBatteryModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true })
        {
            await RunAsync(() => Performance.ApplyPowerModeAsync(PowerSource.OnBattery, CancellationToken.None), "Changing the power mode failed.");
        }
    }

    private async void OnBackgroundAppToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true, DataContext: BackgroundAppDisplay display })
        {
            await RunAsync(() => Performance.ApplyBackgroundAccessAsync(display, CancellationToken.None), $"Changing background access for {display.Name} failed.");
        }
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
