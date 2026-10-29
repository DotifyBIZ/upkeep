using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Performance;

namespace Upkeep.App.ViewModels;

/// <summary>
/// One Store app on the Performance tab. On hands the decision back to Windows ("Power
/// optimized"); off is "Never" — the two choices that mean something for speed.
/// </summary>
public sealed partial class BackgroundAppDisplay : ObservableObject
{
    public BackgroundAppDisplay(BackgroundApp app, string accessLabel)
    {
        ArgumentNullException.ThrowIfNull(app);

        FamilyName = app.PackageFamilyName;
        Name = app.DisplayName;
        AccessLabel = accessLabel;
        IsAllowed = app.Access != BackgroundAccess.Never;
        AppliedAllowed = IsAllowed;
    }

    public string FamilyName { get; }

    public string Name { get; }

    /// <summary>What Settings would call the current state: Always, Power optimized or Never.</summary>
    [ObservableProperty]
    public partial string AccessLabel { get; set; }

    [ObservableProperty]
    public partial bool IsAllowed { get; set; }

    /// <summary>What Windows actually has, so a switch Upkeep moves back isn't taken for the person's.</summary>
    public bool AppliedAllowed { get; set; }
}
