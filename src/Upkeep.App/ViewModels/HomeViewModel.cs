using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Storage;

namespace Upkeep.App.ViewModels;

/// <summary>
/// Home is a starting point, not a dashboard: where the disk stands, what Upkeep did recently, and
/// one way in. No health score, no gamified meter — see the visual direction in docs/design-tokens.md.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IDriveScanner _driveScanner;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    [ObservableProperty]
    public partial string GreetingDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial string UpdateBannerMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public ObservableCollection<DriveDisplay> Drives { get; } = [];

    private string? _releaseUrl;

    public HomeViewModel(
        IDriveScanner driveScanner,
        IUpdateCheckService updateCheckService,
        ILocalizationService localization,
        IAppLogger logger)
    {
        _driveScanner = driveScanner;
        _updateCheckService = updateCheckService;
        _localization = localization;
        _logger = logger;
        GreetingDisplay = _localization.GetString(GreetingKeyFor(DateTime.Now.Hour));
    }

    /// <summary>Which greeting applies, by local hour. Separated out so it is testable without a clock.</summary>
    public static string GreetingKeyFor(int hour) => hour switch
    {
        >= 5 and < 12 => "HomeGreetingMorning",
        >= 12 and < 18 => "HomeGreetingAfternoon",
        >= 18 and < 23 => "HomeGreetingEvening",
        _ => "HomeGreetingNight",
    };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            LoadDrives();
            await CheckForUpdateAsync(cancellationToken);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var drive in _driveScanner.GetFixedDrives())
        {
            string summary = _localization.GetString(
                "DriveFreeOfTotalFormat",
                ByteSize.Format(drive.FreeBytes),
                ByteSize.Format(drive.TotalBytes));

            Drives.Add(new DriveDisplay(drive.Name, summary, drive.UsedFraction));
        }
    }

    private async Task CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _updateCheckService.CheckForUpdateAsync(cancellationToken);
            HasUpdateAvailable = result.IsUpdateAvailable;
            _releaseUrl = result.ReleaseUrl;

            if (result.IsUpdateAvailable && result.LatestVersion is not null)
            {
                UpdateBannerMessage = _localization.GetString("HomeUpdateAvailableFormat", result.LatestVersion);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The check is a convenience; a failure never blocks Home (ADR-0004).
            await _logger.LogWarningAsync($"Home could not check for updates: {ex.Message}", cancellationToken);
        }
    }

    [RelayCommand]
    private async Task OpenReleaseAsync()
    {
        if (_releaseUrl is null)
        {
            return;
        }

        await Windows.System.Launcher.LaunchUriAsync(new Uri(_releaseUrl));
    }
}
