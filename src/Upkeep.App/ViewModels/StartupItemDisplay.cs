using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.ViewModels;

/// <summary>
/// One row in the startup list. The switch reflects what Windows will actually do at the next
/// sign-in, because it is read from — and written to — the same StartupApproved value Task Manager
/// uses.
/// </summary>
public sealed partial class StartupItemDisplay : ObservableObject
{
    public StartupItemDisplay(StartupItem item, string sourceLabel, string? disabledAtDisplay)
    {
        Item = item;
        SourceLabel = sourceLabel;
        DisabledAtDisplay = disabledAtDisplay;
        IsEnabled = item.IsEnabled;
    }

    public StartupItem Item { get; }

    public string Id => Item.Id;

    public string Name => Item.Name;

    /// <summary>Publisher where Windows recorded one, otherwise the command being run — enough for
    /// someone to recognize what a cryptic entry actually is.</summary>
    public string Subtitle => Item.Publisher ?? Item.Command ?? string.Empty;

    /// <summary>Where Windows starts it from, localized.</summary>
    public string SourceLabel { get; }

    /// <summary>When it was turned off, if Windows recorded it.</summary>
    public string? DisabledAtDisplay { get; }

    public bool RequiresElevation => Item.RequiresElevation;

    /// <summary>RunOnce entries can only be removed — they delete themselves after running.</summary>
    public bool CanToggle => Item.CanToggle;

    /// <summary>
    /// Bound two-way to the switch. The view model writes the change through and puts this back if
    /// Windows refuses, so the control never shows a state that didn't happen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }
}
