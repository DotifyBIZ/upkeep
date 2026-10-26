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
    /// <summary>The widest a timing bar is drawn, in pixels; the slowest app fills it.</summary>
    public const double TimingBarMaxWidth = 96;

    public StartupItemDisplay(StartupItem item, string sourceLabel, string? disabledAtDisplay)
    {
        Item = item;
        SourceLabel = sourceLabel;
        DisabledAtDisplay = disabledAtDisplay;
        IsEnabled = item.IsEnabled;
        AppliedEnabled = item.IsEnabled;
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

    /// <summary>
    /// What Windows actually has. The switch's Toggled event also fires when Upkeep moves the
    /// switch itself — putting it back after a refusal, or applying a plan — and comparing against
    /// this is how those are told apart from the person flipping it.
    /// </summary>
    public bool AppliedEnabled { get; set; }

    /// <summary>"2.6 s", "Not timed", or a dash before anything was measured.</summary>
    [ObservableProperty]
    public partial string TimingDisplay { get; set; } = string.Empty;

    /// <summary>Why a row says what it says about timing.</summary>
    [ObservableProperty]
    public partial string TimingTooltip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double TimingBarWidth { get; set; }

    [ObservableProperty]
    public partial string LastStartedDisplay { get; set; } = string.Empty;

    /// <summary>Not started by the person in 30 days — shown as a badge, not a verdict.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecent))]
    public partial bool IsStale { get; set; }

    public bool IsRecent => !IsStale;

    /// <summary>Slow and apparently unused: pre-ticked in the plan, never acted on alone.</summary>
    public bool IsSuggested { get; set; }
}

/// <summary>One line of the "turn these off" preview, which the person can untick.</summary>
public sealed partial class StartupPlanItem : ObservableObject
{
    public StartupPlanItem(StartupItemDisplay display, string detail)
    {
        Display = display;
        Detail = detail;
        IsSelected = true;
    }

    public StartupItemDisplay Display { get; }

    public string Name => Display.Name;

    /// <summary>Measured time and when it was last started, so the decision is made on the facts.</summary>
    public string Detail { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
