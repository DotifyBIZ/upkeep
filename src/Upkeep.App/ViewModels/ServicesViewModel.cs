using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.ViewModels;

/// <summary>One tier and what it means, for the legend under the list.</summary>
public sealed record ServiceTierLegendEntry(string Name, string Description);

/// <summary>Which services the list is showing.</summary>
public enum ServiceFilter
{
    CommonlySafe,
    ThirdParty,
    All,
}

/// <summary>
/// The Services tab: what is set to run in the background, tiered by how safe it is to change.
/// <para>
/// Turning one off sets it to Disabled and records the start type it had, so History can put it
/// back exactly. The shell never changes a service itself — the request goes to the elevated
/// helper, which classifies the service again before acting (ADR-0005).
/// </para>
/// </summary>
public sealed partial class ServicesViewModel : ObservableObject
{
    private readonly IServiceScanner _scanner;
    private readonly IElevationService _elevation;
    private readonly ISessionJournal _journal;
    private readonly ILocalizationService _localization;
    private readonly IAppLogger _logger;

    private readonly List<ServiceItemDisplay> _all = [];

    private SessionManifest? _session;

    public ServicesViewModel(
        IServiceScanner scanner,
        IElevationService elevation,
        ISessionJournal journal,
        ILocalizationService localization,
        IAppLogger logger)
    {
        _scanner = scanner;
        _elevation = elevation;
        _journal = journal;
        _localization = localization;
        _logger = logger;
        SearchText = string.Empty;

        Legend = Enum.GetValues<ServiceTier>()
            .Select(tier => new ServiceTierLegendEntry(
                localization.GetString($"ServicesTier{tier}"),
                localization.GetString($"ServicesTier{tier}Description")))
            .ToList();
    }

    public ObservableCollection<ServiceItemDisplay> Items { get; } = [];

    /// <summary>The four tiers and what each one means. Built once — the wording never changes.</summary>
    public IReadOnlyList<ServiceTierLegendEntry> Legend { get; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial ServiceFilter SelectedFilter { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public string CommonlySafeFilterLabel => _localization.GetString("ServicesFilterCommonlySafeFormat", Count(ServiceFilter.CommonlySafe));

    public string ThirdPartyFilterLabel => _localization.GetString("ServicesFilterThirdPartyFormat", Count(ServiceFilter.ThirdParty));

    public string AllFilterLabel => _localization.GetString("ServicesFilterAllFormat", _all.Count);

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
            var services = await _scanner.ScanAsync(cancellationToken);

            _all.Clear();
            foreach (var service in services.OrderBy(service => service.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                _all.Add(BuildDisplay(service));
            }

            OnPropertyChanged(nameof(CommonlySafeFilterLabel));
            OnPropertyChanged(nameof(ThirdPartyFilterLabel));
            OnPropertyChanged(nameof(AllFilterLabel));
            ApplyFilter();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync("Reading the service list failed.", ex, cancellationToken);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Applies a switch the user just flipped. Off means Disabled; on puts back the start type the
    /// service was found with, so off-then-on leaves the machine as it was.
    /// </summary>
    public async Task ApplyAsync(ServiceItemDisplay display, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (!display.CanToggle)
        {
            // The switch is disabled in the UI; this is the belt to that pair of braces.
            RevertSwitch(display);
            StatusMessage = _localization.GetString("ServicesTierLockedDescription");
            return;
        }

        var availability = await _elevation.EnsureAvailableAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            RevertSwitch(display);
            StatusMessage = _localization.GetString("CommonElevationDeclined");
            return;
        }

        var target = display.IsEnabled ? RestoreStartType(display) : ServiceStartType.Disabled;

        try
        {
            // Journaled before the helper is asked, with the start type to go back to (ADR-0006).
            _session ??= await _journal.StartAsync(SessionKind.Startup, cancellationToken);
            var entry = new ServiceStartTypeChangedEntry(display.Name, display.Item.StartType, target.ToString());
            _session = await _journal.AppendAsync(_session, entry, cancellationToken);
            int entryIndex = _session.Entries.Count - 1;

            var response = await _elevation.SendAsync(new SetServiceStartTypeRequest(display.Name, target), cancellationToken);
            if (response is ServiceChangeResponse { Success: true })
            {
                StatusMessage = null;
                return;
            }

            // The entry describes a change that never happened, so it must not read as one History
            // could put back.
            _session = await _journal.UpdateEntryAsync(
                _session,
                entryIndex,
                entry with { NewStartType = display.Item.StartType },
                cancellationToken);

            RevertSwitch(display);
            StatusMessage = _localization.GetString("ServicesChangeRefusedFormat", display.DisplayName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _logger.LogErrorAsync($"Changing the start type of {display.Name} failed.", ex, cancellationToken);
            RevertSwitch(display);
            StatusMessage = ex.Message;
        }
    }

    /// <summary>Closes the session so History shows one finished session for this visit.</summary>
    public async Task EndSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        await _journal.SaveAsync(_session with { CompletedAt = DateTimeOffset.UtcNow }, cancellationToken);
        _session = null;
    }

    private void ApplyFilter()
    {
        Items.Clear();

        foreach (var display in _all.Where(Matches))
        {
            Items.Add(display);
        }

        IsEmpty = Items.Count == 0;
    }

    private bool Matches(ServiceItemDisplay display)
    {
        bool inTier = SelectedFilter switch
        {
            ServiceFilter.CommonlySafe => display.Tier == ServiceTier.CommonlySafe,
            ServiceFilter.ThirdParty => display.Tier == ServiceTier.ThirdParty,
            _ => true,
        };

        if (!inTier)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        // Both names, because people search for what Services.msc shows and for the key name.
        return display.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
            || display.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private int Count(ServiceFilter filter) => _all.Count(display => filter switch
    {
        ServiceFilter.CommonlySafe => display.Tier == ServiceTier.CommonlySafe,
        ServiceFilter.ThirdParty => display.Tier == ServiceTier.ThirdParty,
        _ => true,
    });

    private ServiceItemDisplay BuildDisplay(ServiceInfo service)
    {
        var tier = ServiceClassifier.Classify(service);
        string state = _localization.GetString(
            "ServicesStateFormat",
            service.StartType,
            _localization.GetString(service.IsRunning ? "ServicesStateRunning" : "ServicesStateStopped"));

        string? whatBreaksKey = ServiceClassifier.WhatBreaksKey(service);

        return new ServiceItemDisplay(
            service,
            tier,
            _localization.GetString($"ServicesTier{tier}"),
            state,
            whatBreaksKey is null ? null : _localization.GetString(whatBreaksKey));
    }

    /// <summary>
    /// What turning a service back on should set it to: what it was found with, or Manual when it
    /// was already disabled and there is nothing to restore.
    /// </summary>
    private static ServiceStartType RestoreStartType(ServiceItemDisplay display)
    {
        var previous = ServiceConfigurator.FromReportedStartType(display.Item.StartType);
        return previous == ServiceStartType.Disabled ? ServiceStartType.Manual : previous;
    }

    /// <summary>Puts the switch back where it was, after the change did not happen.</summary>
    private static void RevertSwitch(ServiceItemDisplay display) => display.IsEnabled = !display.IsEnabled;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFilterChanged(ServiceFilter value) => ApplyFilter();
}
