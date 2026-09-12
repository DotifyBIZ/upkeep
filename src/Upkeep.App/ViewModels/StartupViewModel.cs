using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;

namespace Upkeep.App.ViewModels;

/// <summary>
/// The Startup tab: what runs when Windows starts, and a switch for each one.
/// <para>
/// Turning something off writes the same value Task Manager writes rather than deleting the entry,
/// so it can always be turned back on — and every change is journaled before it happens, so
/// History can revert the lot (ADR-0006).
/// </para>
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupItemScanner _scanner;
    private readonly IStartupItemToggler _toggler;
    private readonly ISessionJournal _journal;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    private SessionManifest? _session;

    public StartupViewModel(
        IStartupItemScanner scanner,
        IStartupItemToggler toggler,
        ISessionJournal journal,
        ILocalizationService localization,
        IAppLogger logger)
    {
        _scanner = scanner;
        _toggler = toggler;
        _journal = journal;
        _localization = localization;
        _logger = logger;
    }

    public ObservableCollection<StartupItemDisplay> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

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
            var items = await _scanner.ScanAsync(cancellationToken);

            Items.Clear();

            foreach (var item in items)
            {
                Items.Add(BuildDisplay(item));
            }

            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading startup items failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Applies a switch the user just flipped. If Windows refuses, the switch goes back rather
    /// than sitting in a state that never took effect.
    /// </summary>
    public async Task ApplyAsync(StartupItemDisplay display, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(display);

        try
        {
            _session ??= await _journal.StartAsync(SessionKind.Startup, cancellationToken);

            (var session, bool success) = await _toggler.SetEnabledAsync(_session, display.Item, display.IsEnabled, cancellationToken);
            _session = session;

            if (!success)
            {
                RevertSwitch(display);
                StatusMessage = _localization.GetString("StartupChangeRefusedFormat", display.Name);
                return;
            }

            StatusMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Changing the startup state of {display.Name} failed.", ex, cancellationToken);
            RevertSwitch(display);
            StatusMessage = ex.Message;
        }
    }

    /// <summary>Closes the session so History shows it as finished. Called when the page goes away.</summary>
    public async Task EndSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        await _journal.SaveAsync(_session with { CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);
        _session = null;
    }

    /// <summary>Puts the switch back where it was, after Windows refused the change.</summary>
    private static void RevertSwitch(StartupItemDisplay display) => display.IsEnabled = !display.IsEnabled;

    private StartupItemDisplay BuildDisplay(StartupItem item)
    {
        string? disabledAt = item.DisabledAt is DateTimeOffset when
            ? _localization.GetString("StartupDisabledAtFormat", when.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : null;

        return new StartupItemDisplay(item, _localization.GetString($"StartupSource{item.Source}"), disabledAt);
    }
}
