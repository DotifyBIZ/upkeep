using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Cleanup;

namespace Upkeep.App.ViewModels;

/// <summary>
/// One row in the cleanup preview: what it is, what cleaning it costs, and whether the user still
/// wants it. Everything here is already localized and formatted — the view binds text, and the
/// decision about what a category *means* stays in Core.
/// </summary>
public sealed partial class CleanupCategoryDisplay : ObservableObject
{
    public CleanupCategoryDisplay(JunkCategoryScan scan, string name, string description, string sizeDisplay, string? note)
    {
        Scan = scan;
        Name = name;
        Description = description;
        SizeDisplay = sizeDisplay;
        Note = note;
        IsSelected = JunkCatalog.Get(scan.CategoryId).SelectedByDefault && !scan.IsEmpty;
    }

    public JunkCategoryScan Scan { get; }

    public JunkCategoryId CategoryId => Scan.CategoryId;

    public string Name { get; }

    public string Description { get; }

    /// <summary>Formatted size, or a phrase when only Windows knows it (component store cleanup).</summary>
    public string SizeDisplay { get; }

    /// <summary>Why this category found less than expected — a running browser, missing rights.</summary>
    public string? Note { get; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>True for the two operations Windows cannot undo, which the row labels as such.</summary>
    public bool IsIrreversible => JunkCatalog.Get(Scan.CategoryId).Removal == RemovalKind.Irreversible;

    public bool RequiresElevation => JunkCatalog.Get(Scan.CategoryId).RequiresElevation;

    /// <summary>Nothing to do, so the row is shown but not selectable.</summary>
    public bool IsEmpty => Scan.IsEmpty;

    /// <summary>
    /// A row can only be ticked once there is something to tick it for. That covers both an empty
    /// category and a machine-wide one nobody has measured yet — the latter has no size to show
    /// and no file list to approve, so ticking it would promise something the preview can't back.
    /// </summary>
    public bool IsSelectable => !IsEmpty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public long TotalBytes => Scan.TotalBytes;
}
