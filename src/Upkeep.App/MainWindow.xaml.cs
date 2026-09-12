using Microsoft.UI.Xaml;

namespace Upkeep.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        RootFrame.Navigate(typeof(MainPage));
    }
}
