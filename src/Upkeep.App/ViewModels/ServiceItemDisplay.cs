using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Services;

namespace Upkeep.App.ViewModels;

/// <summary>
/// One service as the Services tab shows it: what it is, what tier it falls in, and the honest
/// one-line answer to "what stops working?" where Upkeep has one.
/// </summary>
public sealed partial class ServiceItemDisplay : ObservableObject
{
    public ServiceItemDisplay(ServiceInfo item, ServiceTier tier, string tierLabel, string stateLabel, string? whatBreaks)
    {
        Item = item;
        Tier = tier;
        TierLabel = tierLabel;
        StateLabel = stateLabel;
        WhatBreaks = whatBreaks;
        IsEnabled = !string.Equals(item.StartType, "Disabled", StringComparison.OrdinalIgnoreCase);
    }

    public ServiceInfo Item { get; }

    public ServiceTier Tier { get; }

    /// <summary>The service key name, which is what the helper is asked to change.</summary>
    public string Name => Item.Name;

    /// <summary>What Windows calls it in Services.msc — the name someone will recognize.</summary>
    public string DisplayName => Item.DisplayName;

    /// <summary>Localized tier name: commonly safe, third-party, leave alone, locked.</summary>
    public string TierLabel { get; }

    /// <summary>Start type and whether it is running right now, as one line.</summary>
    public string StateLabel { get; }

    /// <summary>
    /// What the user loses by turning this one off. Only curated services have one; everything
    /// else shows the tier's general wording instead, because Upkeep has nothing honest to say.
    /// </summary>
    public string? WhatBreaks { get; }

    public bool HasWhatBreaks => !string.IsNullOrEmpty(WhatBreaks);

    /// <summary>False for services Windows needs — the switch is there but not operable.</summary>
    public bool CanToggle => ServiceClassifier.CanChange(Item);

    /// <summary>
    /// Bound two-way to the switch. Off means Disabled; on puts back the start type the service
    /// had, so turning something off and on again leaves it as it was found.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }
}
