using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Cleanup;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Safety;
using Upkeep.App.Core.Settings;

namespace Upkeep.App.ViewModels;

/// <summary>
/// Drives the Cleanup page through its three states: scan, preview, results.
/// <para>
/// Scanning never changes anything, and machine-wide categories are not even measured until the
/// user asks for them — that request is what raises the single UAC prompt, and what gives the
/// preview real sizes to show instead of "needs administrator rights" (ADR-0005, ADR-0006).
/// </para>
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly IJunkScanner _scanner;
    private readonly ICleanupExecutor _executor;
    private readonly IElevationService _elevation;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly IMessenger _messenger;
    private readonly IAppSettingsService _settings;
    private readonly IWellKnownPaths _paths;

    public CleanupViewModel(
        IJunkScanner scanner,
        ICleanupExecutor executor,
        IElevationService elevation,
        ILocalizationService localization,
        IAppLogger logger,
        IMessenger messenger,
        IAppSettingsService settings,
        IWellKnownPaths paths)
    {
        _scanner = scanner;
        _executor = executor;
        _elevation = elevation;
        _localization = localization;
        _logger = logger;
        _messenger = messenger;
        _settings = settings;
        _paths = paths;
        Categories.CollectionChanged += (_, _) => RefreshSelectionSummary();
    }

    public ObservableCollection<CleanupCategoryDisplay> Categories { get; } = [];

    /// <summary>The two groups the preview shows, split by whether cleaning needs administrator
    /// rights — the distinction the user actually has to make a decision about.</summary>
    public ObservableCollection<CleanupCategoryDisplay> UserCategories { get; } = [];

    public ObservableCollection<CleanupCategoryDisplay> SystemCategories { get; } = [];

    /// <summary>The user's own rules, as they typed them. Empty for almost everyone.</summary>
    public ObservableCollection<string> CustomRules { get; } = [];

    [ObservableProperty]
    public partial string NewRule { get; set; } = string.Empty;

    /// <summary>Why the last rule wasn't accepted, or null when it was.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuleProblem))]
    public partial string? RuleProblem { get; set; }

    public bool HasRuleProblem => !string.IsNullOrEmpty(RuleProblem);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScanPrompt))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScanPrompt))]
    [NotifyPropertyChangedFor(nameof(CanIncludeSystemItems))]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPreview))]
    public partial bool ShowResults { get; set; }

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanClean { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIncludeSystemItems))]
    public partial bool SystemItemsIncluded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    /// <summary>The page shows either the preview or the results, never both.</summary>
    public bool ShowPreview => !ShowResults;

    /// <summary>Before the first scan, the page explains what scanning does and offers the button.</summary>
    public bool ShowScanPrompt => !HasScanned && !IsScanning;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>
    /// Offered only after a scan, and only until the machine-wide categories have been measured —
    /// this is the action that raises the session's single UAC prompt.
    /// </summary>
    public bool CanIncludeSystemItems => HasScanned && !SystemItemsIncluded;

    // Results
    [ObservableProperty]
    public partial string FreedDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FreeSpaceDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ItemsRemovedDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ItemsSkippedDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestorePointNote))]
    public partial string? RestorePointDisplay { get; set; }

    public bool HasRestorePointNote => !string.IsNullOrEmpty(RestorePointDisplay);

    /// <summary>What the confirmation dialog needs to describe, and the executor needs to run.</summary>
    public CleanupPlan BuildPlan() => CleanupPlan.From(
        [.. Categories.Select(category => category.Scan)],
        new HashSet<JunkCategoryId>(Categories.Where(category => category.IsSelected).Select(category => category.CategoryId)));

    [RelayCommand]
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ShowResults = false;
        StatusMessage = null;

        try
        {
            DetachSelectionHandlers();
            Categories.Clear();
            UserCategories.Clear();
            SystemCategories.Clear();

            foreach (var scan in await _scanner.ScanUserCategoriesAsync(cancellationToken))
            {
                Add(scan);
            }

            await ScanCustomRulesAsync(cancellationToken);

            // Machine-wide categories appear straight away, so the user can see what exists before
            // deciding whether to grant anything — they just have no size until they ask.
            foreach (var category in JunkCatalog.ForScope(JunkScope.System))
            {
                Add(JunkCategoryScan.Empty(category.Id, JunkScanNote.NeedsElevation));
            }

            HasScanned = true;
            SystemItemsIncluded = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("The cleanup scan failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
            RefreshSelectionSummary();
        }
    }

    /// <summary>
    /// Measures the machine-wide categories. This is where the session's single UAC prompt happens
    /// — asked for deliberately by the user, rather than sprung on them at launch.
    /// </summary>
    [RelayCommand]
    public async Task IncludeSystemItemsAsync(CancellationToken cancellationToken)
    {
        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            StatusMessage = _localization.GetString("CommonElevationDeclined");
            return;
        }

        IsScanning = true;
        try
        {
            foreach (var category in JunkCatalog.ForScope(JunkScope.System))
            {
                var response = await _elevation.SendAsync(new ScanJunkCategoryRequest(category.Id), cancellationToken);
                if (response is JunkScanResponse scanned)
                {
                    Replace(scanned.Scan);
                }
            }

            SystemItemsIncluded = true;
            StatusMessage = null;
        }
        finally
        {
            IsScanning = false;
            RefreshSelectionSummary();
        }
    }

    /// <summary>Runs the plan the user confirmed. The page owns the confirmation dialog; by the
    /// time this runs, the answer was yes.</summary>
    [RelayCommand]
    public async Task CleanAsync(CancellationToken cancellationToken)
    {
        var plan = BuildPlan();
        if (plan.IsEmpty || IsRunning)
        {
            return;
        }

        IsRunning = true;
        try
        {
            var outcome = await _executor.ExecuteAsync(plan, progress: null, cancellationToken);
            ShowOutcome(outcome);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("The cleanup run failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    public async Task DoneAsync(CancellationToken cancellationToken)
    {
        ShowResults = false;
        await ScanAsync(cancellationToken);
    }

    /// <summary>
    /// Accepts a rule the user typed, or says why it can't be. A rule decides what gets
    /// quarantined, so what it points at is checked here (<see cref="CustomCleanupRule"/>) and not
    /// left for the scan to discover.
    /// </summary>
    [RelayCommand]
    public async Task AddRuleAsync(CancellationToken cancellationToken)
    {
        if (!CustomCleanupRule.TryParse(NewRule, _paths, out var rule, out var problem))
        {
            RuleProblem = _localization.GetString($"CustomRuleProblem{problem}");
            return;
        }

        if (CustomRules.Contains(rule.Display, StringComparer.OrdinalIgnoreCase))
        {
            RuleProblem = _localization.GetString($"CustomRuleProblem{CustomRuleProblem.Duplicate}");
            return;
        }

        CustomRules.Add(rule.Display);
        NewRule = string.Empty;
        RuleProblem = null;

        await SaveRulesAsync(cancellationToken);
    }

    [RelayCommand]
    public async Task RemoveRuleAsync(string? rule)
    {
        if (rule is null || !CustomRules.Remove(rule))
        {
            return;
        }

        RuleProblem = null;
        await SaveRulesAsync(CancellationToken.None);
    }

    /// <summary>Reads the saved rules and measures what they match, as its own category in the
    /// preview. Categories with nothing in them are dropped from the plan anyway, so a user with
    /// no rules never sees the row at all.</summary>
    private async Task ScanCustomRulesAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        var rules = CustomCleanupRule.ParseAll(settings.CustomCleanupRules, _paths);

        CustomRules.Clear();
        foreach (var rule in rules)
        {
            CustomRules.Add(rule.Display);
        }

        if (rules.Count == 0)
        {
            return;
        }

        Add(await _scanner.ScanCustomRulesAsync(rules, cancellationToken));
    }

    private async Task SaveRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken);
            settings.CustomCleanupRules = [.. CustomRules];
            await _settings.SaveAsync(settings, cancellationToken);

            // The preview has to agree with the rules it was built from, so what the new rule
            // matches is measured now rather than at the next scan.
            if (HasScanned)
            {
                var rules = CustomCleanupRule.ParseAll(CustomRules, _paths);
                var scan = rules.Count == 0
                    ? JunkCategoryScan.Empty(JunkCategoryId.CustomRules)
                    : await _scanner.ScanCustomRulesAsync(rules, cancellationToken);

                if (Categories.Any(category => category.CategoryId == JunkCategoryId.CustomRules))
                {
                    Replace(scan);
                }
                else
                {
                    Add(scan);
                }

                RefreshSelectionSummary();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Saving the custom cleanup rules failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
    }

    private void ShowOutcome(CleanupOutcome outcome)
    {
        FreedDisplay = _localization.GetString("CleanupResultsFreedFormat", ByteSize.Format(outcome.FreedBytes));

        FreeSpaceDisplay = outcome.SystemDriveFreeBytes is long free
            ? _localization.GetString("CleanupResultsFreeSpaceFormat", ByteSize.Format(free))
            : string.Empty;

        ItemsRemovedDisplay = outcome.ItemsRemoved.ToString(System.Globalization.CultureInfo.CurrentCulture);
        ItemsSkippedDisplay = outcome.ItemsSkipped.ToString(System.Globalization.CultureInfo.CurrentCulture);

        RestorePointDisplay = outcome.RestorePoint switch
        {
            { Status: RestorePointStatus.Created } point when point.ProtectionWasEnabled =>
                _localization.GetString("CleanupResultsRestorePointEnabledFormat", point.Description ?? string.Empty),
            { Status: RestorePointStatus.Created } point => point.Description,
            { Status: RestorePointStatus.ReusedRecent } point =>
                _localization.GetString("CleanupResultsRestorePointReusedFormat", point.Description ?? string.Empty),
            { Status: RestorePointStatus.Unavailable } => _localization.GetString("CleanupResultsRestorePointUnavailable"),
            _ => null,
        };

        StatusMessage = outcome.ElevationDeclined ? _localization.GetString("CommonElevationDeclined") : null;
        ShowResults = true;

        // A run started here can finish while the user is three pages away; the shell decides
        // whether that is worth a toast, since only it knows where they went.
        _messenger.Send(new CleanupFinishedMessage(FreedDisplay));
    }

    private void Add(JunkCategoryScan scan)
    {
        var display = BuildDisplay(scan);
        display.PropertyChanged += OnCategoryPropertyChanged;
        Categories.Add(display);
        GroupFor(display).Add(display);
    }

    private void Replace(JunkCategoryScan scan)
    {
        for (int index = 0; index < Categories.Count; index++)
        {
            if (Categories[index].CategoryId != scan.CategoryId)
            {
                continue;
            }

            var previous = Categories[index];
            previous.PropertyChanged -= OnCategoryPropertyChanged;

            var display = BuildDisplay(scan);
            display.PropertyChanged += OnCategoryPropertyChanged;
            Categories[index] = display;

            var group = GroupFor(display);
            int groupIndex = group.IndexOf(previous);
            if (groupIndex >= 0)
            {
                group[groupIndex] = display;
            }

            return;
        }
    }

    private ObservableCollection<CleanupCategoryDisplay> GroupFor(CleanupCategoryDisplay display) =>
        display.RequiresElevation ? SystemCategories : UserCategories;

    private CleanupCategoryDisplay BuildDisplay(JunkCategoryScan scan)
    {
        var category = JunkCatalog.Get(scan.CategoryId);

        string sizeDisplay = scan.Note switch
        {
            JunkScanNote.SizeKnownAfterCleanup => _localization.GetString("CleanupSizeAfterCleanup"),
            JunkScanNote.NeedsElevation => _localization.GetString("CleanupSizeNeedsAdmin"),
            _ => ByteSize.Format(scan.TotalBytes),
        };

        string? note = scan.Note switch
        {
            JunkScanNote.BrowserRunning => _localization.GetString("JunkNoteBrowserRunningFormat", string.Join(", ", scan.Details)),
            JunkScanNote.SizeKnownAfterCleanup => _localization.GetString("JunkNoteSizeKnownAfterCleanup"),
            JunkScanNote.NeedsElevation => _localization.GetString("JunkNoteNeedsElevation"),
            JunkScanNote.PartiallyUnreadable => _localization.GetString("JunkNotePartiallyUnreadableFormat", scan.SkippedCount),
            _ => null,
        };

        return new CleanupCategoryDisplay(
            scan,
            _localization.GetString(category.NameKey),
            _localization.GetString(category.DescriptionKey),
            sizeDisplay,
            note);
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupCategoryDisplay.IsSelected))
        {
            RefreshSelectionSummary();
        }
    }

    private void DetachSelectionHandlers()
    {
        foreach (var category in Categories)
        {
            category.PropertyChanged -= OnCategoryPropertyChanged;
        }
    }

    private void RefreshSelectionSummary()
    {
        var selected = Categories.Where(category => category.IsSelected).ToList();
        long bytes = selected.Sum(category => category.TotalBytes);

        SelectionSummary = _localization.GetString(
            "CleanupSelectionSummaryFormat",
            selected.Count,
            Categories.Count,
            ByteSize.Format(bytes));

        CanClean = selected.Count > 0 && !IsRunning;
    }
}
