using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Apps;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// The uninstaller. Pressing Uninstall runs the app's own uninstaller; clearing what it left
/// behind is a second, separate decision with its own preview (README, ADR-0006).
/// </summary>
public sealed partial class AppsPage : Page
{
    private readonly ILocalizationService _localization;

    public AppsPage()
    {
        ViewModel = App.Services.GetRequiredService<AppsViewModel>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        InitializeComponent();
        Loaded += AppsPage_Loaded;
    }

    public AppsViewModel ViewModel { get; }

    private async void AppsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it.
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The Apps page failed to load.", ex);
        }
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.Filter(sender.Text);
        }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: InstalledAppDisplay app })
            {
                return;
            }

            var outcome = await ViewModel.UninstallAsync(app, CancellationToken.None);
            if (outcome is { Status: UninstallLaunchStatus.Completed, StillInstalled: false, Leftovers.Count: > 0 })
            {
                await OfferLeftoversAsync(app, outcome.Leftovers);
            }
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Uninstalling failed to start.", ex);
        }
    }

    /// <summary>
    /// Shows exactly what was found, ticked but reviewable, and removes only what survives the
    /// user's review. Nothing here is implied by having pressed Uninstall.
    /// </summary>
    private async Task OfferLeftoversAsync(InstalledAppDisplay app, IReadOnlyList<LeftoverItem> leftovers)
    {
        var checkBoxes = new List<(CheckBox Box, LeftoverItem Item)>();
        var content = new StackPanel { Spacing = 16 };

        content.Children.Add(new TextBlock
        {
            Text = _localization.GetString("AppsLeftoversBody"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        });

        AddSection(content, checkBoxes, "AppsLeftoversFoldersHeader", leftovers.Where(item => item.Kind == LeftoverKind.Folder));
        AddSection(content, checkBoxes, "AppsLeftoversRegistryHeader", leftovers.Where(item => item.Kind == LeftoverKind.RegistryKey));

        content.Children.Add(new TextBlock
        {
            Text = _localization.GetString("AppsLeftoversNote"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["Gray500Brush"],
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localization.GetString("AppsLeftoversTitleFormat", app.Name),
            Content = new ScrollViewer { Content = content, MaxHeight = 420 },
            PrimaryButtonText = _localization.GetString("AppsLeftoversPrimaryFormat", leftovers.Count),
            CloseButtonText = _localization.GetString("AppsLeftoversKeep"),
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources["PrimaryButtonStyle"],
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var chosen = checkBoxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.Item).ToList();
        await ViewModel.RemoveLeftoversAsync(app, chosen, CancellationToken.None);
    }

    private void AddSection(
        StackPanel content,
        List<(CheckBox Box, LeftoverItem Item)> checkBoxes,
        string headerKey,
        IEnumerable<LeftoverItem> items)
    {
        var section = items.ToList();
        if (section.Count == 0)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = _localization.GetString(headerKey),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
        });

        foreach (var item in section)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var box = new CheckBox { IsChecked = true, MinWidth = 0 };

            row.Children.Add(box);
            row.Children.Add(new TextBlock
            {
                Text = item.SizeBytes > 0 ? $"{item.Path}  ({ByteSize.Format(item.SizeBytes)})" : item.Path,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (item.RequiresElevation)
            {
                // Machine-wide items need the elevated helper, which this page does not use yet —
                // so they are shown, unticked, rather than silently dropped from the list.
                box.IsChecked = false;
                box.IsEnabled = false;
                // A shield always and only means "needs administrator approval" in this app. Written
                // as an escape rather than a literal private-use character, which survives only as
                // long as nothing re-encodes the file.
                row.Children.Add(new FontIcon
                {
                    Glyph = "",
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["Secondary600Brush"],
                });
            }

            checkBoxes.Add((box, item));
            panel.Children.Add(row);
        }

        content.Children.Add(panel);
    }
}
