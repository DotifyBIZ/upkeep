using Upkeep.App.Core.Apps;

namespace Upkeep.App.ViewModels;

/// <summary>
/// One row in the uninstaller list: everything already localized and formatted, plus the small
/// tinted initial that stands in for an icon (reading icons out of arbitrary third-party
/// executables is a decoding surface this app has no reason to take on).
/// </summary>
public sealed record InstalledAppDisplay(
    InstalledApp App,
    string Name,
    string Subtitle,
    string SourceLabel,
    string ScopeLabel,
    string SizeDisplay,
    string InstalledDisplay,
    string Initial,
    string Tint)
{
    public string Id => App.Id;

    public bool IsStoreApp => App.Source == AppSource.Store;

    /// <summary>Sort key for "largest first"; apps that don't report a size sort last.</summary>
    public long SizeBytes => App.EstimatedBytes ?? 0;

    /// <summary>Whether this row matches what the user typed in the search box.</summary>
    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || (App.Publisher?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
}
