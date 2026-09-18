using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Views;

/// <summary>
/// Junk and cache cleanup. Scanning never changes anything: it produces a plan the user reviews,
/// and the confirmation below is the last step before any of it runs (ADR-0006).
/// </summary>
public sealed partial class CleanupPage : Page
{
    private readonly ILocalizationService _localization;

    public CleanupPage()
    {
        ViewModel = App.Services.GetRequiredService<CleanupViewModel>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        InitializeComponent();
    }

    public CleanupViewModel ViewModel { get; }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        // async void is unavoidable on a XAML handler, so nothing escapes it: an exception here
        // would take the process down rather than fail one action.
        try
        {
            var plan = ViewModel.BuildPlan();
            if (plan.IsEmpty)
            {
                return;
            }

            if (await ConfirmAsync(plan) != ContentDialogResult.Primary)
            {
                return;
            }

            await ViewModel.CleanAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("The cleanup run could not start.", ex);
        }
    }

    // The row's own rule is its DataContext, which is how the button knows which one it removes.
    private async void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RemoveRuleAsync((sender as FrameworkElement)?.DataContext as string);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Removing a custom cleanup rule failed.", ex);
        }
    }

    // Typing a rule and pressing Enter is what anyone does with a box and an Add button next to it.
    private async void CustomRule_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;

        try
        {
            await ViewModel.AddRuleAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await App.Services.GetRequiredService<IAppLogger>().LogErrorAsync("Adding a custom cleanup rule failed.", ex);
        }
    }

    /// <summary>
    /// Says exactly what is about to happen — the UAC prompt, the restore point, and the fact that
    /// some of it can't be undone — before anything happens.
    /// </summary>
    private async Task<ContentDialogResult> ConfirmAsync(Upkeep.App.Core.Cleanup.CleanupPlan plan)
    {
        var lines = new StackPanel { Spacing = 12 };

        if (plan.RequiresElevation)
        {
            lines.Children.Add(BuildLine("", _localization.GetString("CleanupConfirmElevation")));
        }

        if (plan.NeedsRestorePoint)
        {
            lines.Children.Add(BuildLine("", _localization.GetString("CleanupConfirmRestorePoint")));
        }

        lines.Children.Add(BuildLine(
            "",
            _localization.GetString(plan.HasIrreversibleWork ? "CleanupConfirmIrreversible" : "CleanupConfirmDeleted")));

        // Custom rules match the user's own files, which go to quarantine rather than being
        // deleted — the opposite promise from the line above, so it gets said rather than implied.
        if (plan.HasQuarantinedWork)
        {
            lines.Children.Add(BuildLine("", _localization.GetString("CleanupConfirmQuarantined")));
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localization.GetString("CleanupConfirmTitleFormat", ByteSize.Format(plan.EstimatedBytes)),
            Content = lines,
            PrimaryButtonText = _localization.GetString("CleanupConfirmPrimary"),
            CloseButtonText = _localization.GetString("CleanupConfirmCancel"),
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonStyle = (Style)Application.Current.Resources["PrimaryButtonStyle"],
        };

        return await dialog.ShowAsync();
    }

    private static StackPanel BuildLine(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        panel.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Top,
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
        });

        return panel;
    }
}
