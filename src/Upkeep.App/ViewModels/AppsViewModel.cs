using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Apps;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;

namespace Upkeep.App.ViewModels;

/// <summary>
/// The uninstaller list, and what happens after an app is removed.
/// <para>
/// Upkeep never deletes an app itself: it runs the app's own uninstaller, waits, and only then
/// offers to clear what that app left behind. The leftover step is a separate, explicit decision
/// with its own preview — never part of pressing Uninstall.
/// </para>
/// </summary>
public sealed partial class AppsViewModel : ObservableObject
{
    /// <summary>Tints for the initial badge, from the design tokens' neutral and accent surfaces.</summary>
    private static readonly string[] Tints = ["#FEE2E2", "#EEEEF8", "#DCFCE7", "#FFEDD5", "#F3F4F6", "#DBEAFE"];

    private readonly IInstalledAppScanner _scanner;
    private readonly IAppUninstaller _uninstaller;
    private readonly ILeftoverRemover _leftoverRemover;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    private readonly List<InstalledAppDisplay> _allApps = [];

    public AppsViewModel(
        IInstalledAppScanner scanner,
        IAppUninstaller uninstaller,
        ILeftoverRemover leftoverRemover,
        ILocalizationService localization,
        IAppLogger logger)
    {
        _scanner = scanner;
        _uninstaller = uninstaller;
        _leftoverRemover = leftoverRemover;
        _localization = localization;
        _logger = logger;
    }

    public ObservableCollection<InstalledAppDisplay> Apps { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InstalledAppDisplay? SelectedApp { get; set; }

    [ObservableProperty]
    public partial bool IsUninstalling { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Filters the list as the user types. Purely a view concern — nothing is re-scanned.</summary>
    public void Filter(string query)
    {
        Apps.Clear();
        foreach (var app in _allApps.Where(app => app.Matches(query)))
        {
            Apps.Add(app);
        }

        SummaryText = _localization.GetString("AppsCountFormat", Apps.Count);
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = null;

        try
        {
            var apps = await _scanner.ScanAsync(cancellationToken);

            _allApps.Clear();
            _allApps.AddRange(apps.Select(BuildDisplay));
            Filter(string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading the installed app list failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Runs the app's own uninstaller and reports what came back. The page decides what to show —
    /// including whether to offer the leftovers it found.
    /// </summary>
    public async Task<UninstallOutcome?> UninstallAsync(InstalledAppDisplay app, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (IsUninstalling)
        {
            return null;
        }

        IsUninstalling = true;
        StatusMessage = _localization.GetString("AppsUninstallRunningFormat", app.Name);

        try
        {
            var outcome = await _uninstaller.UninstallAsync(app.App, cancellationToken);

            StatusMessage = outcome switch
            {
                { Status: UninstallLaunchStatus.Declined } => _localization.GetString("AppsUninstallDeclined"),
                { Status: UninstallLaunchStatus.Failed } => _localization.GetString("AppsUninstallFailedFormat", outcome.Detail ?? string.Empty),
                { StillInstalled: true } => _localization.GetString("AppsStillInstalledFormat", app.Name),
                { Leftovers.Count: 0 } => _localization.GetString("AppsNoLeftoversFormat", app.Name),
                _ => null,
            };

            if (!outcome.StillInstalled && outcome.Status == UninstallLaunchStatus.Completed)
            {
                RemoveFromList(app);
            }

            return outcome;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Uninstalling {app.Name} failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    /// <summary>Removes the leftovers the user ticked, after the app itself is gone.</summary>
    public async Task RemoveLeftoversAsync(InstalledAppDisplay app, IReadOnlyList<LeftoverItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return;
        }

        try
        {
            var outcome = await _leftoverRemover.RemoveAsync(app.App, items, cancellationToken);

            StatusMessage = outcome.FailedCount > 0
                ? _localization.GetString("AppsLeftoversFailedFormat", outcome.FailedCount)
                : _localization.GetString(
                    "AppsLeftoversRemovedFormat",
                    outcome.RemovedCount,
                    ByteSize.Format(outcome.QuarantinedBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Removing leftovers of {app.Name} failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
    }

    private void RemoveFromList(InstalledAppDisplay app)
    {
        _allApps.RemoveAll(candidate => candidate.Id == app.Id);
        Apps.Remove(app);
        SummaryText = _localization.GetString("AppsCountFormat", Apps.Count);
    }

    private InstalledAppDisplay BuildDisplay(InstalledApp app)
    {
        string subtitle = string.Join(
            " · ",
            new[] { app.Publisher, app.Version }.Where(part => !string.IsNullOrWhiteSpace(part)));

        string sizeDisplay = app.EstimatedBytes is long bytes
            ? ByteSize.Format(bytes)
            : _localization.GetString("AppsSizeUnknown");

        string installedDisplay = app.InstalledOn is DateOnly installed
            ? _localization.GetString("AppsInstalledOnFormat", installed.ToString("d", CultureInfo.CurrentCulture))
            : string.Empty;

        string initial = string.IsNullOrEmpty(app.DisplayName)
            ? "?"
            : app.DisplayName.TrimStart()[..1].ToUpperInvariant();

        return new InstalledAppDisplay(
            app,
            app.DisplayName,
            subtitle,
            _localization.GetString(app.Source == AppSource.Store ? "AppsSourceStore" : "AppsSourceDesktop"),
            _localization.GetString(app.Scope == AppScope.AllUsers ? "AppsScopeAllUsers" : "AppsScopeCurrentUser"),
            sizeDisplay,
            installedDisplay,
            initial,
            // Stable per app so a row keeps its colour between scans.
            Tints[Math.Abs(app.DisplayName.GetHashCode(StringComparison.Ordinal)) % Tints.Length]);
    }
}
