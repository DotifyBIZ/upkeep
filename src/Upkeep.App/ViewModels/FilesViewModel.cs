using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Files;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;

namespace Upkeep.App.ViewModels;

/// <summary>
/// The Files page: duplicates, large and forgotten files, and where the space actually went.
/// <para>
/// All three are read-only until the user picks something and confirms. Removal goes through
/// <see cref="IFileActionExecutor"/>, which quarantines rather than deletes unless the user
/// explicitly says otherwise (ADR-0006).
/// </para>
/// </summary>
public sealed partial class FilesViewModel : ObservableObject
{
    /// <summary>Canvas the treemap is laid out for; the page hosts exactly this size.</summary>
    public const double TreemapWidth = 560;

    public const double TreemapHeight = 468;

    /// <summary>Navy tints from the design tokens, darkest for the biggest node.</summary>
    private static readonly string[] TreemapFills = ["#D9D9EF", "#B8B7E0", "#9695CF"];

    private readonly IDuplicateFinder _duplicateFinder;
    private readonly ILargeFileFinder _largeFileFinder;
    private readonly IDiskUsageScanner _diskUsageScanner;
    private readonly IFileActionExecutor _fileActions;
    private readonly IFolderPickerService _folderPicker;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    public FilesViewModel(
        IDuplicateFinder duplicateFinder,
        ILargeFileFinder largeFileFinder,
        IDiskUsageScanner diskUsageScanner,
        IFileActionExecutor fileActions,
        IFolderPickerService folderPicker,
        ILocalizationService localization,
        IAppLogger logger)
    {
        _duplicateFinder = duplicateFinder;
        _largeFileFinder = largeFileFinder;
        _diskUsageScanner = diskUsageScanner;
        _fileActions = fileActions;
        _folderPicker = folderPicker;
        _localization = localization;
        _logger = logger;

        foreach (var folder in DefaultFolders())
        {
            Folders.Add(folder);
        }
    }

    /// <summary>Where the scans look. The user's own folders, never the whole drive by default.</summary>
    public ObservableCollection<string> Folders { get; } = [];

    public ObservableCollection<DuplicateGroupDisplay> DuplicateGroups { get; } = [];

    public ObservableCollection<LargeFileDisplay> LargeFiles { get; } = [];

    public ObservableCollection<UsageRectDisplay> TreemapRects { get; } = [];

    public ObservableCollection<UsageRowDisplay> UsageRows { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanRemove { get; set; }

    [ObservableProperty]
    public partial bool DeletePermanently { get; set; }

    [ObservableProperty]
    public partial bool HasScannedDuplicates { get; set; }

    [ObservableProperty]
    public partial bool HasScannedLargeFiles { get; set; }

    [ObservableProperty]
    public partial string UsageBreadcrumb { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UsageSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? UsageUnreadableNote { get; set; }

    /// <summary>Smallest file any scan considers. Duplicated tiny files are noise, not findings.</summary>
    [ObservableProperty]
    public partial long MinimumFileBytes { get; set; } = 1024 * 1024;

    [ObservableProperty]
    public partial long LargeFileThresholdBytes { get; set; } = 250L * 1024 * 1024;

    [ObservableProperty]
    public partial int ForgottenAfterDays { get; set; } = 180;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    private FileScanScope BuildScope() => new()
    {
        Roots = [.. Folders],
        MinimumFileBytes = MinimumFileBytes,
    };

    [RelayCommand]
    public async Task AddFolderAsync(CancellationToken cancellationToken)
    {
        string? folder = await _folderPicker.PickFolderAsync(cancellationToken);
        if (!string.IsNullOrEmpty(folder) && !Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            Folders.Add(folder);
        }
    }

    public void RemoveFolder(string folder) => Folders.Remove(folder);

    [RelayCommand]
    public async Task ScanDuplicatesAsync(CancellationToken cancellationToken)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusMessage = null;
        try
        {
            var result = await _duplicateFinder.FindAsync(BuildScope(), progress: null, cancellationToken);

            DetachDuplicateHandlers();
            DuplicateGroups.Clear();

            foreach (var group in result.Groups)
            {
                DuplicateGroups.Add(BuildGroupDisplay(group));
            }

            SummaryText = _localization.GetString(
                "FilesDuplicateSummaryFormat",
                result.Groups.Count,
                ByteSize.Format(result.ReclaimableBytes));

            HasScannedDuplicates = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportFailureAsync("The duplicate scan failed.", ex, cancellationToken);
        }
        finally
        {
            IsScanning = false;
            RefreshSelection();
        }
    }

    [RelayCommand]
    public async Task ScanLargeFilesAsync(CancellationToken cancellationToken)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusMessage = null;
        try
        {
            var criteria = new LargeFileCriteria
            {
                MinimumBytes = LargeFileThresholdBytes,
                MinimumAge = TimeSpan.FromDays(ForgottenAfterDays),
            };

            var results = await _largeFileFinder.FindAsync(BuildScope(), criteria, progress: null, cancellationToken);

            DetachLargeFileHandlers();
            LargeFiles.Clear();

            foreach (var result in results)
            {
                var display = new LargeFileDisplay(
                    result,
                    ByteSize.Format(result.SizeBytes),
                    _localization.GetString("FilesModifiedFormat", result.File.LastWriteUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)),
                    ReasonFor(result));

                display.PropertyChanged += OnSelectionChanged;
                LargeFiles.Add(display);
            }

            SummaryText = _localization.GetString(
                "FilesLargeSummaryFormat",
                results.Count,
                ByteSize.Format(results.Sum(result => result.SizeBytes)));

            HasScannedLargeFiles = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportFailureAsync("The large-file scan failed.", ex, cancellationToken);
        }
        finally
        {
            IsScanning = false;
            RefreshSelection();
        }
    }

    [RelayCommand]
    public async Task ScanUsageAsync(CancellationToken cancellationToken)
    {
        if (IsScanning || Folders.Count == 0)
        {
            return;
        }

        IsScanning = true;
        StatusMessage = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _diskUsageScanner.ScanAsync(Folders[0], progress: null, cancellationToken);
            stopwatch.Stop();

            ShowUsage(result, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportFailureAsync("The disk usage scan failed.", ex, cancellationToken);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Draws one level of the tree; clicking a folder re-draws it from there.</summary>
    public void ShowUsage(DiskUsageResult result, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(result);

        UsageBreadcrumb = result.Root.Path;
        UsageSummary = _localization.GetString(
            "FilesUsageScannedFormat",
            result.FilesExamined,
            $"{elapsed.TotalSeconds:0} s");

        UsageUnreadableNote = result.UnreadableFolders > 0
            ? _localization.GetString("FilesUsageUnreadableFormat", result.UnreadableFolders)
            : null;

        TreemapRects.Clear();
        UsageRows.Clear();

        var children = result.Root.Children;
        long total = Math.Max(result.Root.SizeBytes, 1);

        foreach (var rect in TreemapLayout.Squarify(children, TreemapWidth, TreemapHeight))
        {
            double share = rect.Node.SizeBytes / (double)total;
            TreemapRects.Add(new UsageRectDisplay(
                DisplayNameFor(rect.Node),
                rect.Node.Path,
                ByteSize.Format(rect.Node.SizeBytes),
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                TreemapFills[Math.Min((int)(share * TreemapFills.Length), TreemapFills.Length - 1)],
                // A label in a rectangle too small for it is just noise on the canvas.
                rect.Width > 70 && rect.Height > 34));
        }

        foreach (var child in children.Take(8))
        {
            UsageRows.Add(new UsageRowDisplay(
                DisplayNameFor(child),
                child.Path,
                ByteSize.Format(child.SizeBytes),
                child.SizeBytes / (double)total,
                child.IsDirectory));
        }
    }

    [RelayCommand]
    public async Task RemoveSelectedAsync(CancellationToken cancellationToken)
    {
        var paths = SelectedPaths().ToList();
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var outcome = await _fileActions.RemoveAsync(new FileActionRequest(paths, DeletePermanently), cancellationToken);

            SummaryText = outcome.WasQuarantined
                ? _localization.GetString("FilesRemovedSummaryFormat", outcome.RemovedCount, ByteSize.Format(outcome.AffectedBytes))
                : _localization.GetString("FilesDeletedSummaryFormat", outcome.RemovedCount, ByteSize.Format(outcome.FreedBytes));

            StatusMessage = outcome.FailedCount > 0
                ? _localization.GetString("FilesFailedSummaryFormat", outcome.FailedCount)
                : null;

            RemoveHandledFiles(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportFailureAsync("Removing the selected files failed.", ex, cancellationToken);
        }
        finally
        {
            RefreshSelection();
        }
    }

    /// <summary>Everything currently ticked, across whichever list the user is looking at.</summary>
    public IEnumerable<string> SelectedPaths() =>
        DuplicateGroups.SelectMany(group => group.Files).Where(file => file.IsSelected).Select(file => file.Path)
            .Concat(LargeFiles.Where(file => file.IsSelected).Select(file => file.Path));

    private void RemoveHandledFiles(IReadOnlyCollection<string> paths)
    {
        var handled = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        foreach (var file in LargeFiles.Where(file => handled.Contains(file.Path)).ToList())
        {
            file.PropertyChanged -= OnSelectionChanged;
            LargeFiles.Remove(file);
        }

        // A group whose extras are gone has nothing left to offer.
        foreach (var group in DuplicateGroups.ToList())
        {
            if (group.Files.All(file => file.IsKeeper || handled.Contains(file.Path)))
            {
                foreach (var file in group.Files)
                {
                    file.PropertyChanged -= OnSelectionChanged;
                }

                DuplicateGroups.Remove(group);
            }
        }
    }

    private DuplicateGroupDisplay BuildGroupDisplay(DuplicateGroup group)
    {
        var files = new List<DuplicateFileDisplay>();
        foreach (var file in group.Files)
        {
            var display = new DuplicateFileDisplay(
                file,
                file.Path == group.Keep.Path,
                _localization.GetString("FilesModifiedFormat", file.LastWriteUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));

            display.PropertyChanged += OnSelectionChanged;
            files.Add(display);
        }

        return new DuplicateGroupDisplay(
            group,
            group.Keep.Name,
            _localization.GetString("FilesCopyCountFormat", group.CopyCount, ByteSize.Format(group.SizeBytes)),
            files);
    }

    private string ReasonFor(LargeFileResult result) => _localization.GetString(
        result switch
        {
            { IsLarge: true, IsOld: true } => "FilesReasonBoth",
            { IsLarge: true } => "FilesReasonLarge",
            _ => "FilesReasonOld",
        });

    private static string DisplayNameFor(UsageNode node) => node.Name;

    private void OnSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DuplicateFileDisplay.IsSelected) or nameof(LargeFileDisplay.IsSelected))
        {
            RefreshSelection();
        }
    }

    private void DetachDuplicateHandlers()
    {
        foreach (var file in DuplicateGroups.SelectMany(group => group.Files))
        {
            file.PropertyChanged -= OnSelectionChanged;
        }
    }

    private void DetachLargeFileHandlers()
    {
        foreach (var file in LargeFiles)
        {
            file.PropertyChanged -= OnSelectionChanged;
        }
    }

    private void RefreshSelection()
    {
        var selected = DuplicateGroups.SelectMany(group => group.Files).Where(file => file.IsSelected).Select(file => file.SizeBytes)
            .Concat(LargeFiles.Where(file => file.IsSelected).Select(file => file.SizeBytes))
            .ToList();

        SelectionSummary = _localization.GetString(
            "FilesSelectionSummaryFormat",
            selected.Count,
            ByteSize.Format(selected.Sum()));

        CanRemove = selected.Count > 0 && !IsScanning;
    }

    private async Task ReportFailureAsync(string message, Exception exception, CancellationToken cancellationToken)
    {
        await _logger.LogErrorAsync(message, exception, cancellationToken);
        StatusMessage = exception.Message;
    }

    /// <summary>The folders people actually keep their own files in.</summary>
    private static IEnumerable<string> DefaultFolders()
    {
        Environment.SpecialFolder[] folders =
        [
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.DesktopDirectory,
        ];

        foreach (var folder in folders)
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                yield return path;
            }
        }

        // Downloads has no SpecialFolder entry, and it is where duplicates actually accumulate.
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            yield return downloads;
        }
    }
}
