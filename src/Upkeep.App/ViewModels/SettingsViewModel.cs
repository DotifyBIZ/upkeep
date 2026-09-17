using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Formatting;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Settings;

namespace Upkeep.App.ViewModels;

/// <summary>
/// One language the user can pick. The tag is what gets persisted; empty means "follow Windows".
/// </summary>
public sealed record LanguageChoice(string Tag, string Label);

/// <summary>
/// The Settings tab: the handful of preferences Upkeep keeps, all of them local.
/// <para>
/// Each control writes straight through to the settings file rather than waiting for an Apply
/// button — there is nothing here where a half-applied state would be meaningful.
/// </para>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Where the source and releases live — the only link the About section offers.</summary>
    public const string ProjectUrl = "https://github.com/DotifyBIZ/upkeep";

    /// <summary>The bounds FileAppSettingsService clamps to; offering more would silently revert.</summary>
    public const int MinimumRetentionDays = 1;

    public const int MaximumRetentionDays = 90;

    /// <summary>How much of the log the viewer shows. Enough to cover the run that just went
    /// wrong; the full files are one button away for anything older.</summary>
    public const int LogLinesShown = 200;

    private readonly IAppSettingsService _settings;
    private readonly IUpdateCheckService _updates;
    private readonly IQuarantineStore _quarantine;
    private readonly IWindowsUiLauncher _launcher;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    private AppSettings _current = new();
    private string? _releaseUrl;
    private bool _loading;

    public SettingsViewModel(
        IAppSettingsService settings,
        IUpdateCheckService updates,
        IQuarantineStore quarantine,
        IWindowsUiLauncher launcher,
        ILocalizationService localization,
        IAppLogger logger,
        string? productVersion = null)
    {
        _settings = settings;
        _updates = updates;
        _quarantine = quarantine;
        _launcher = launcher;
        _localization = localization;
        _logger = logger;

        Languages =
        [
            new LanguageChoice(string.Empty, localization.GetString("SettingsLanguageSystem")),
            new LanguageChoice("en-US", localization.GetString("SettingsLanguageEnglish")),
            new LanguageChoice("pl-PL", localization.GetString("SettingsLanguagePolish")),
        ];

        VersionDisplay = BuildVersionDisplay(productVersion);
    }

    /// <summary>The three choices the settings file will actually accept.</summary>
    public IReadOnlyList<LanguageChoice> Languages { get; }

    [ObservableProperty]
    public partial LanguageChoice? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdatesEnabled { get; set; }

    [ObservableProperty]
    public partial int RetentionDays { get; set; }

    [ObservableProperty]
    public partial string? QuarantineHolding { get; set; }

    [ObservableProperty]
    public partial string? UpdateStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingForUpdates { get; set; }

    /// <summary>True once a check found a newer release, so the page can offer to open it.</summary>
    [ObservableProperty]
    public partial bool HasRelease { get; set; }

    /// <summary>The tail of the diagnostic log, as one block of text.</summary>
    [ObservableProperty]
    public partial string LogLines { get; set; } = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>The version this build reports, or a plain note when it was never stamped.</summary>
    public string VersionDisplay { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Set through the guard so reading the saved values doesn't look like the user changing them.
        _loading = true;

        try
        {
            _current = await _settings.LoadAsync(cancellationToken);

            SelectedLanguage = Languages.FirstOrDefault(choice => choice.Tag == _current.LanguageOverride) ?? Languages[0];
            CheckForUpdatesEnabled = _current.CheckForUpdatesEnabled;
            RetentionDays = Math.Clamp(_current.QuarantineRetentionDays, MinimumRetentionDays, MaximumRetentionDays);
            RefreshQuarantineSize();
            StatusMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading the settings failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Persists whatever the controls currently say. The language change is deliberately not
    /// applied live: it is resolved once at startup, and pretending otherwise would leave half the
    /// window in the old language.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_loading)
        {
            return;
        }

        _current.LanguageOverride = SelectedLanguage?.Tag ?? string.Empty;
        _current.CheckForUpdatesEnabled = CheckForUpdatesEnabled;
        _current.QuarantineRetentionDays = Math.Clamp(RetentionDays, MinimumRetentionDays, MaximumRetentionDays);

        try
        {
            await _settings.SaveAsync(_current, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Saving the settings failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
    }

    /// <summary>Checks for a newer release on demand, whatever the weekly setting says.</summary>
    public async Task CheckForUpdatesNowAsync(CancellationToken cancellationToken)
    {
        if (IsCheckingForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        HasRelease = false;

        try
        {
            var result = await _updates.CheckForUpdateAsync(cancellationToken);
            _releaseUrl = result.ReleaseUrl;

            if (result.IsUpdateAvailable && result.LatestVersion is not null)
            {
                UpdateStatus = _localization.GetString("SettingsUpdateAvailableFormat", result.LatestVersion);
                HasRelease = result.ReleaseUrl is not null;
                return;
            }

            UpdateStatus = _localization.GetString("SettingsUpdateUpToDate");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // A failed check is a normal outcome for a tool that works offline, not an error worth
            // a stack trace in the user's face.
            await _logger.LogWarningAsync($"The update check could not complete: {ex.Message}", cancellationToken);
            UpdateStatus = _localization.GetString("SettingsUpdateCheckFailed");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>Deletes everything in quarantine now, rather than waiting for it to age out.</summary>
    public void EmptyQuarantineNow()
    {
        long freed = _quarantine.PurgeAll();

        RefreshQuarantineSize();
        StatusMessage = _localization.GetString("SettingsQuarantineEmptiedFormat", ByteSize.Format(freed));
    }

    /// <summary>
    /// Fills <see cref="LogLines"/> from the diagnostic log. Called when the user opens the log
    /// section rather than on page load: reading the file has no reason to happen for the people
    /// who never look at it.
    /// </summary>
    public async Task LoadLogAsync(CancellationToken cancellationToken)
    {
        var lines = await _logger.ReadRecentAsync(LogLinesShown, cancellationToken);

        LogLines = lines.Count == 0
            ? _localization.GetString("SettingsLogEmpty")
            : string.Join(Environment.NewLine, lines);
    }

    [RelayCommand]
    public void OpenLogsFolder() => Open(_logger.LogDirectory);

    [RelayCommand]
    public void OpenProjectPage() => Open(ProjectUrl);

    [RelayCommand]
    public void OpenRelease()
    {
        if (_releaseUrl is not null)
        {
            Open(_releaseUrl);
        }
    }

    private void RefreshQuarantineSize() =>
        QuarantineHolding = _localization.GetString("SettingsQuarantineHoldingFormat", ByteSize.Format(_quarantine.GetTotalBytes()));

    private void Open(string target)
    {
        if (!_launcher.Open(target))
        {
            StatusMessage = _localization.GetString("SettingsCouldNotOpen");
        }
    }

    /// <summary>
    /// Release builds carry a version stamped at publish time. A local build has none worth
    /// showing, and printing "0.0.0" as though it meant something would be worse than saying so.
    /// </summary>
    private string BuildVersionDisplay(string? productVersion)
    {
        string? version = productVersion
            ?? typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SettingsViewModel).Assembly.GetName().Version?.ToString();

        // Strip the "+abc1234" source-revision suffix the SDK appends.
        if (version is not null && version.IndexOf('+', StringComparison.Ordinal) is int plus && plus > 0)
        {
            version = version[..plus];
        }

        return string.IsNullOrWhiteSpace(version) || version.StartsWith("0.0.0", StringComparison.Ordinal)
            ? _localization.GetString("SettingsVersionUnknown")
            : _localization.GetString("SettingsVersionFormat", version);
    }
}
