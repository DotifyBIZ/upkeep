using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class ServicesViewModelTests : IDisposable
{
    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-services-vm-{Guid.NewGuid():N}");
    private readonly FakeServiceScanner _scanner = new();
    private readonly FakeElevationService _elevation = new();
    private readonly SessionJournal _journal;

    public ServicesViewModelTests() => _journal = new SessionJournal(_journalDirectory);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _journal.Dispose();

        try
        {
            Directory.Delete(_journalDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private ServicesViewModel CreateViewModel() =>
        new(_scanner, _elevation, _journal, new FakeLocalizationService(), new FakeAppLogger());

    /// <summary>A curated service — the tier the tab opens on.</summary>
    private static ServiceInfo Curated(string name = "DiagTrack", string startType = "Manual", bool isRunning = false) =>
        new(name, $"{name} display name", startType, isRunning, IsMicrosoft: true);

    private static ServiceInfo ThirdParty(string name = "VendorSvc") =>
        new(name, $"{name} display name", "Automatic", IsRunning: true, IsMicrosoft: false);

    private static ServiceInfo Locked(string name = "RpcSs") =>
        new(name, $"{name} display name", "Automatic", IsRunning: true, IsMicrosoft: true);

    private async Task<ServicesViewModel> LoadedAsync()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);
        return viewModel;
    }

    [Fact]
    public async Task LoadAsync_OpensOnTheCommonlySafeServices()
    {
        // The tab that is safe to act on is the one you land on; everything else is a filter away.
        _scanner.Services.Add(Curated());
        _scanner.Services.Add(ThirdParty());
        _scanner.Services.Add(Locked());

        var viewModel = await LoadedAsync();

        Assert.Equal("DiagTrack", Assert.Single(viewModel.Items).Name);
    }

    [Fact]
    public async Task LoadAsync_CuratedService_SaysWhatStopsWorking()
    {
        _scanner.Services.Add(Curated());

        var viewModel = await LoadedAsync();

        var item = viewModel.Items[0];
        Assert.Equal("ServicesTierCommonlySafe", item.TierLabel);
        Assert.Equal("ServiceBreaksDiagTrack", item.WhatBreaks);
        Assert.True(item.HasWhatBreaks);
    }

    [Fact]
    public async Task LoadAsync_ServiceUpkeepHasNothingHonestToSayAbout_ShowsNoWhatBreaksLine()
    {
        _scanner.Services.Add(ThirdParty());

        var viewModel = await LoadedAsync();
        viewModel.SelectedFilter = ServiceFilter.ThirdParty;

        Assert.Null(viewModel.Items[0].WhatBreaks);
        Assert.False(viewModel.Items[0].HasWhatBreaks);
    }

    [Fact]
    public async Task LoadAsync_ShowsStartTypeAndWhetherItIsRunning()
    {
        _scanner.Services.Add(Curated(startType: "Automatic", isRunning: true));

        var viewModel = await LoadedAsync();

        Assert.Equal("ServicesStateFormat(Automatic|ServicesStateRunning)", viewModel.Items[0].StateLabel);
    }

    [Fact]
    public async Task LoadAsync_ServiceWindowsNeeds_CannotBeToggled()
    {
        _scanner.Services.Add(Locked());

        var viewModel = await LoadedAsync();
        viewModel.SelectedFilter = ServiceFilter.All;

        Assert.False(viewModel.Items.Single(item => item.Name == "RpcSs").CanToggle);
    }

    [Fact]
    public async Task Filters_CarryTheCountForEachTier()
    {
        _scanner.Services.Add(Curated());
        _scanner.Services.Add(ThirdParty());
        _scanner.Services.Add(Locked());

        var viewModel = await LoadedAsync();

        Assert.Equal("ServicesFilterCommonlySafeFormat(1)", viewModel.CommonlySafeFilterLabel);
        Assert.Equal("ServicesFilterThirdPartyFormat(1)", viewModel.ThirdPartyFilterLabel);
        Assert.Equal("ServicesFilterAllFormat(3)", viewModel.AllFilterLabel);
    }

    [Fact]
    public async Task SearchText_MatchesTheKeyNameAsWellAsTheDisplayName()
    {
        // People search for what Services.msc shows them and for the short key name.
        _scanner.Services.Add(Curated());
        var viewModel = await LoadedAsync();
        viewModel.SelectedFilter = ServiceFilter.All;

        viewModel.SearchText = "diagtrack";
        Assert.Single(viewModel.Items);

        viewModel.SearchText = "display name";
        Assert.Single(viewModel.Items);

        viewModel.SearchText = "nothing matches this";
        Assert.Empty(viewModel.Items);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task ApplyAsync_SwitchedOff_AsksTheHelperToDisableItAndRecordsWhatItWas()
    {
        _scanner.Services.Add(Curated());
        var viewModel = await LoadedAsync();

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        var request = Assert.IsType<SetServiceStartTypeRequest>(Assert.Single(_elevation.SentRequests));
        Assert.Equal("DiagTrack", request.ServiceName);
        Assert.Equal(ServiceStartType.Disabled, request.StartType);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));
        var entry = Assert.IsType<ServiceStartTypeChangedEntry>(Assert.Single(session.Entries));
        Assert.Equal("Manual", entry.PreviousStartType);
        Assert.Equal("Disabled", entry.NewStartType);
    }

    [Fact]
    public async Task ApplyAsync_SwitchedBackOn_PutsBackTheStartTypeItWasFoundWith()
    {
        // Off then on should leave the machine as it was found, not as Upkeep would have set it.
        _scanner.Services.Add(Curated(startType: "Automatic"));
        var viewModel = await LoadedAsync();

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        viewModel.Items[0].IsEnabled = true;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        var request = Assert.IsType<SetServiceStartTypeRequest>(_elevation.SentRequests[1]);
        Assert.Equal(ServiceStartType.Automatic, request.StartType);
    }

    [Fact]
    public async Task ApplyAsync_ServiceThatWasAlreadyDisabled_ComesBackAsManual()
    {
        // There is no earlier start type to restore, and Manual is the setting that lets whatever
        // needs it start it again.
        _scanner.Services.Add(Curated(startType: "Disabled"));
        var viewModel = await LoadedAsync();

        viewModel.Items[0].IsEnabled = true;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        var request = Assert.IsType<SetServiceStartTypeRequest>(Assert.Single(_elevation.SentRequests));
        Assert.Equal(ServiceStartType.Manual, request.StartType);
    }

    [Fact]
    public async Task ApplyAsync_ServiceWindowsNeeds_IsNeverSentToTheHelper()
    {
        _scanner.Services.Add(Locked());
        var viewModel = await LoadedAsync();
        viewModel.SelectedFilter = ServiceFilter.All;

        var locked = viewModel.Items.Single(item => item.Name == "RpcSs");
        locked.IsEnabled = false;
        await viewModel.ApplyAsync(locked, CancellationToken.None);

        Assert.Empty(_elevation.SentRequests);
        Assert.True(locked.IsEnabled);
        Assert.Equal("ServicesTierLockedDescription", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ApplyAsync_ElevationDeclined_PutsTheSwitchBackAndChangesNothing()
    {
        _scanner.Services.Add(Curated());
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);
        var viewModel = await LoadedAsync();

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        Assert.Empty(_elevation.SentRequests);
        Assert.True(viewModel.Items[0].IsEnabled);
        Assert.Equal("CommonElevationDeclined", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ApplyAsync_HelperRefused_PutsTheSwitchBackAndTheEntryReadsAsNoChange()
    {
        // An entry describing a change that never happened would be one History offers to undo.
        _scanner.Services.Add(Curated());
        _elevation.ServiceChangeSucceeds = false;
        var viewModel = await LoadedAsync();

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        Assert.True(viewModel.Items[0].IsEnabled);
        Assert.Contains("ServicesChangeRefusedFormat", viewModel.StatusMessage, StringComparison.Ordinal);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));
        var entry = Assert.IsType<ServiceStartTypeChangedEntry>(Assert.Single(session.Entries));
        Assert.Equal(entry.PreviousStartType, entry.NewStartType);
    }

    [Fact]
    public async Task ApplyAsync_SeveralChanges_ShareOneSession()
    {
        _scanner.Services.Add(Curated());
        _scanner.Services.Add(Curated("MapsBroker"));
        var viewModel = await LoadedAsync();

        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);
        await viewModel.ApplyAsync(viewModel.Items[1], CancellationToken.None);

        Assert.Single(await _journal.ListAsync(CancellationToken.None));
        Assert.Equal(2, _elevation.SentRequests.Count);
    }

    [Fact]
    public async Task EndSessionAsync_ClosesTheSessionForHistory()
    {
        _scanner.Services.Add(Curated());
        var viewModel = await LoadedAsync();
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        await viewModel.EndSessionAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task EndSessionAsync_NothingWasChanged_WritesNoSession()
    {
        var viewModel = await LoadedAsync();

        await viewModel.EndSessionAsync(CancellationToken.None);

        Assert.Empty(await _journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public void Legend_ExplainsEveryTier()
    {
        // The page shows this instead of asking people to infer what a tier means.
        var viewModel = CreateViewModel();

        Assert.Equal(4, viewModel.Legend.Count);
        Assert.Contains(viewModel.Legend, entry => entry.Name == "ServicesTierLocked");
        Assert.Contains(viewModel.Legend, entry => entry.Description == "ServicesTierLockedDescription");
    }
}
