using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Abstractions;

namespace Upkeep.App.ViewModels;

/// <summary>One thing the command palette can jump to. Tag matches MainPage's own nav tags.</summary>
public sealed record CommandPaletteItem(string Tag, string Title);

/// <summary>
/// Backs the Ctrl+K palette (MainPage.xaml): every section, searchable by name. Scope is
/// deliberately just navigation for now — the same jump one click on the rail already gives, just
/// reachable without leaving the keyboard. Actions that change something (pause updates, revert a
/// session) stay where they are rather than growing a second, unaudited path to the same effect.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    // Tag values match MainPage.PageFor exactly; title keys reuse the same resources the
    // NavigationView items already show, so the palette can never name a section differently than
    // the rail does.
    private static readonly (string Tag, string TitleKey)[] Sections =
    [
        ("Home", "NavHome.Content"),
        ("Cleanup", "NavCleanup.Content"),
        ("Files", "NavFiles.Content"),
        ("Apps", "NavApps.Content"),
        ("Startup", "NavStartup.Content"),
        ("Drivers", "NavDrivers.Content"),
        ("History", "NavHistory.Content"),
        ("Settings", "NavSettings.Content"),
    ];

    private readonly IReadOnlyList<CommandPaletteItem> _allItems;

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    public ObservableCollection<CommandPaletteItem> Results { get; } = [];

    public CommandPaletteViewModel(ILocalizationService localization)
    {
        _allItems = [.. Sections.Select(section => new CommandPaletteItem(section.Tag, localization.GetString(section.TitleKey)))];
        Refresh();
    }

    /// <summary>Clears the search box and shows every section again — called each time the palette opens.</summary>
    public void Reset()
    {
        Query = string.Empty;
    }

    partial void OnQueryChanged(string value) => Refresh();

    /// <summary>Case-insensitive substring match, in the sections' own order — kept as a static
    /// method so the matching rule is testable without a localization service or a live collection.</summary>
    public static IEnumerable<CommandPaletteItem> Filter(IReadOnlyList<CommandPaletteItem> items, string query) =>
        string.IsNullOrWhiteSpace(query)
            ? items
            : items.Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void Refresh()
    {
        Results.Clear();
        foreach (var item in Filter(_allItems, Query))
        {
            Results.Add(item);
        }

        HasNoResults = Results.Count == 0;
    }
}
