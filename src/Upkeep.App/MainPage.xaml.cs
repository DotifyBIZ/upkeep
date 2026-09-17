using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;
using Upkeep.App.Views;
using Windows.System;

namespace Upkeep.App;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        PaletteViewModel = App.Services.GetRequiredService<CommandPaletteViewModel>();
        WelcomeViewModel = App.Services.GetRequiredService<WelcomeViewModel>();
        InitializeComponent();
        ContentFrame.Navigated += ContentFrame_Navigated;
        ContentFrame.Navigate(typeof(HomePage));

        // Settings asks for the tour from inside the frame; the dialog belongs to the window.
        WeakReferenceMessenger.Default.Register<MainPage, ShowWelcomeMessage>(this, (page, _) => page.ShowWelcome());

        Loaded += MainPage_Loaded;
    }

    public CommandPaletteViewModel PaletteViewModel { get; }

    public WelcomeViewModel WelcomeViewModel { get; }

    private static Type PageFor(string tag) => tag switch
    {
        "Home" => typeof(HomePage),
        "Cleanup" => typeof(CleanupPage),
        "Files" => typeof(FilesPage),
        "Apps" => typeof(AppsPage),
        "Startup" => typeof(StartupPage),
        "Drivers" => typeof(DriversPage),
        "History" => typeof(HistoryPage),
        "Settings" => typeof(SettingsPage),
        _ => typeof(HomePage),
    };

    private void Navigate(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        var target = PageFor(tag);
        if (ContentFrame.CurrentSourcePageType != target)
        {
            ContentFrame.Navigate(target);
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            Navigate(item.Tag as string);
        }
    }

    // Fires even when the invoked item is already selected — without it, clicking the highlighted
    // rail item while on a page reached from inside that section does nothing at all.
    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            Navigate(item.Tag as string);
        }
    }

    private void Nav_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void CommandPaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        PaletteViewModel.Reset();
        CommandPaletteDialog.XamlRoot = XamlRoot;

        // Not awaited: ShowAsync doesn't resolve until the dialog closes, and this handler has
        // nothing left to do once it's open — the fire-and-forget is the point, not an oversight.
        _ = CommandPaletteDialog.ShowAsync();
    }

    // Focus has to wait for Opened rather than happen right after ShowAsync is called: the dialog's
    // content isn't loaded yet at that point, so a Focus() call there silently lands nowhere.
    private void CommandPaletteDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        CommandPaletteInput.Focus(FocusState.Programmatic);

    private void CommandPaletteInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                NavigateToAndClosePalette(PaletteViewModel.Results.FirstOrDefault());
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                CommandPaletteDialog.Hide();
                e.Handled = true;
                break;
        }
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await WelcomeViewModel.ShouldShowOnLaunchAsync(CancellationToken.None))
            {
                ShowWelcome();
            }
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Deciding whether to show the welcome tour failed.", ex);
        }
    }

    private void ShowWelcome()
    {
        WelcomeViewModel.Reset();
        WelcomeDialog.XamlRoot = XamlRoot;

        // Not awaited for the same reason the palette isn't: ShowAsync only resolves on close.
        _ = WelcomeDialog.ShowAsync();
    }

    // "Next" on anything but the last screen advances instead of closing, which is what cancelling
    // the click does — the dialog's own primary button is the tour's forward control.
    private void WelcomeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!WelcomeViewModel.IsLastStep)
        {
            args.Cancel = true;
            WelcomeViewModel.Next();
        }
    }

    // Closed rather than the button handlers: skipping, finishing and pressing Escape all mean the
    // same thing — this machine's owner has seen it and shouldn't be shown it again.
    private async void WelcomeDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        try
        {
            await WelcomeViewModel.MarkSeenAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Recording that the welcome tour was seen failed.", ex);
        }
    }

    private void CommandPaletteList_ItemClick(object sender, ItemClickEventArgs e) =>
        NavigateToAndClosePalette(e.ClickedItem as CommandPaletteItem);

    private void NavigateToAndClosePalette(CommandPaletteItem? item)
    {
        if (item is null)
        {
            return;
        }

        CommandPaletteDialog.Hide();
        Navigate(item.Tag);
    }

    // Keeps the rail honest about where the user actually is, including after a GoBack or a
    // navigation started from inside a page rather than from the rail.
    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        Nav.IsBackEnabled = ContentFrame.CanGoBack;

        string? tag = e.SourcePageType switch
        {
            var type when type == typeof(HomePage) => "Home",
            var type when type == typeof(CleanupPage) => "Cleanup",
            var type when type == typeof(FilesPage) => "Files",
            var type when type == typeof(AppsPage) => "Apps",
            var type when type == typeof(StartupPage) => "Startup",
            var type when type == typeof(DriversPage) => "Drivers",
            var type when type == typeof(HistoryPage) => "History",
            var type when type == typeof(SettingsPage) => "Settings",
            _ => null,
        };

        if (tag is null)
        {
            return;
        }

        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if ((item.Tag as string) == tag)
            {
                Nav.SelectedItem = item;
                return;
            }
        }
    }
}
