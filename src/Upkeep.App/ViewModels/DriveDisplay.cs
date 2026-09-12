namespace Upkeep.App.ViewModels;

/// <summary>One drive card on Home: already formatted and localized, so the view binds text.</summary>
public sealed record DriveDisplay(string Name, string SpaceSummary, double UsedFraction);
