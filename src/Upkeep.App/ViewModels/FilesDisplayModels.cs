using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Files;

namespace Upkeep.App.ViewModels;

/// <summary>One file inside a duplicate group, with the tick that decides its fate.</summary>
public sealed partial class DuplicateFileDisplay : ObservableObject
{
    public DuplicateFileDisplay(ScannedFile file, bool isKeeper, string modifiedDisplay)
    {
        File = file;
        IsKeeper = isKeeper;
        ModifiedDisplay = modifiedDisplay;

        // The copy being kept starts unticked; the extras start ticked, which is what the user
        // came to the page to do — but every one of them is still theirs to change.
        IsSelected = !isKeeper;
    }

    public ScannedFile File { get; }

    public string Path => File.Path;

    /// <summary>True for the copy Upkeep suggests keeping — the oldest one.</summary>
    public bool IsKeeper { get; }

    public string ModifiedDisplay { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public long SizeBytes => File.SizeBytes;
}

/// <summary>A set of identical files, and what removing the extras would give back.</summary>
public sealed class DuplicateGroupDisplay
{
    public DuplicateGroupDisplay(DuplicateGroup group, string name, string summary, IReadOnlyList<DuplicateFileDisplay> files)
    {
        Group = group;
        Name = name;
        Summary = summary;
        Files = files;
    }

    public DuplicateGroup Group { get; }

    public string Name { get; }

    /// <summary>"3 copies · 1.1 GB each" — already localized and formatted.</summary>
    public string Summary { get; }

    public IReadOnlyList<DuplicateFileDisplay> Files { get; }
}

/// <summary>One oversized or forgotten file.</summary>
public sealed partial class LargeFileDisplay : ObservableObject
{
    public LargeFileDisplay(LargeFileResult result, string sizeDisplay, string modifiedDisplay, string reason)
    {
        Result = result;
        SizeDisplay = sizeDisplay;
        ModifiedDisplay = modifiedDisplay;
        Reason = reason;
    }

    public LargeFileResult Result { get; }

    public string Path => Result.File.Path;

    public string Name => Result.File.Name;

    public string SizeDisplay { get; }

    public string ModifiedDisplay { get; }

    /// <summary>Why it is on the list: large, forgotten, or both.</summary>
    public string Reason { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public long SizeBytes => Result.SizeBytes;
}

/// <summary>One rectangle on the treemap, already laid out by Core.</summary>
public sealed record UsageRectDisplay(
    string Name,
    string Path,
    string SizeDisplay,
    double X,
    double Y,
    double Width,
    double Height,
    string Fill,
    bool ShowLabel);

/// <summary>One row in the "largest here" list beside the treemap.</summary>
public sealed record UsageRowDisplay(string Name, string Path, string SizeDisplay, double Share, bool IsDirectory);
