using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Upkeep.App.Views;

/// <summary>
/// Junk and cache cleanup. Scanning never changes anything: it produces a plan the user reviews
/// before any of it executes (docs/adr/0006-safety-model.md).
/// </summary>
public sealed partial class CleanupPage : Page
{
    public CleanupPage() => InitializeComponent();

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        // The scanner lands next; until then the button must not pretend to do something.
        ScanButton.IsEnabled = false;
        ScanProgress.IsActive = true;
    }
}
