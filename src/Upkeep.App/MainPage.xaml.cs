using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Upkeep.App.Views;

namespace Upkeep.App;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        ContentFrame.Navigated += ContentFrame_Navigated;
        ContentFrame.Navigate(typeof(HomePage));
    }

    private static Type PageFor(string tag) => tag switch
    {
        "Home" => typeof(HomePage),
        "Cleanup" => typeof(CleanupPage),
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

    // Keeps the rail honest about where the user actually is, including after a GoBack or a
    // navigation started from inside a page rather than from the rail.
    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        Nav.IsBackEnabled = ContentFrame.CanGoBack;

        string? tag = e.SourcePageType switch
        {
            var type when type == typeof(HomePage) => "Home",
            var type when type == typeof(CleanupPage) => "Cleanup",
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
