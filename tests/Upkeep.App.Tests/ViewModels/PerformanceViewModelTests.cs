using Upkeep.App.Core.Elevation;
using Upkeep.App.Core.Performance;
using Upkeep.App.Core.Platform;
using Upkeep.App.Core.Services;
using Upkeep.App.Core.Sessions;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class PerformanceViewModelTests : IDisposable
{
    private static readonly Guid BalancedId = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformanceId = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-perf-vm-{Guid.NewGuid():N}");
    private readonly FakePerformanceSettings _settings = new();
    private readonly FakeServiceScanner _scanner = new();
    private readonly FakeElevationService _elevation = new();
    private readonly FakeWindowsUiLauncher _launcher = new();
    private readonly SessionJournal _journal;

    public PerformanceViewModelTests() => _journal = new SessionJournal(_journalDirectory);

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

    private PerformanceViewModel CreateViewModel() =>
        new(_settings, _scanner, _elevation, _launcher, _journal, new FakeLocalizationService(), new FakeAppLogger());

    private async Task<PerformanceViewModel> LoadedAsync()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);
        return viewModel;
    }

    private void GiveTwoPowerPlans() =>
        _settings.State = new PerformanceState(
            AnimationsEnabled: true,
            TransparencyEnabled: true,
            PowerPlans:
            [
                new PowerPlan(BalancedId, "Balanced", IsActive: true),
                new PowerPlan(HighPerformanceId, "High performance", IsActive: false),
            ]);

    [Fact]
    public async Task LoadAsync_ShowsWhatWindowsIsCurrentlySetTo()
    {
        _settings.State = new PerformanceState(AnimationsEnabled: false, TransparencyEnabled: true, PowerPlans: []);

        var viewModel = await LoadedAsync();

        Assert.False(viewModel.AnimationsEnabled);
        Assert.True(viewModel.TransparencyEnabled);
    }

    [Fact]
    public async Task LoadAsync_SelectsThePlanWindowsIsRunning()
    {
        GiveTwoPowerPlans();

        var viewModel = await LoadedAsync();

        Assert.Equal(2, viewModel.PowerPlans.Count);
        Assert.Equal("Balanced", viewModel.SelectedPowerPlan?.Name);
        Assert.True(viewModel.HasPowerPlanChoice);
    }

    [Fact]
    public async Task LoadAsync_OnlyOnePlanOffered_TheChoiceIsNotPresented()
    {
        // Plenty of PCs only expose Balanced; a dropdown of one is a dead control.
        _settings.State = new PerformanceState(true, true, [new PowerPlan(BalancedId, "Balanced", IsActive: true)]);

        var viewModel = await LoadedAsync();

        Assert.False(viewModel.HasPowerPlanChoice);
    }

    [Fact]
    public async Task LoadAsync_ReadsIndexingFromTheWindowsSearchService()
    {
        _scanner.Services.Add(new ServiceInfo("WSearch", "Windows Search", "Disabled", IsRunning: false, IsMicrosoft: true));

        var viewModel = await LoadedAsync();

        Assert.False(viewModel.IndexingEnabled);
    }

    [Fact]
    public async Task LoadAsync_MachineWithoutWindowsSearch_ReadsAsOffRatherThanFailing()
    {
        var viewModel = await LoadedAsync();

        Assert.False(viewModel.IndexingEnabled);
        Assert.Null(viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadAsync_ReadingTheCurrentState_IsNotTreatedAsAChange()
    {
        // Everything is applied by an explicit call, so loading must journal nothing at all.
        _scanner.Services.Add(new ServiceInfo("WSearch", "Windows Search", "Automatic", IsRunning: true, IsMicrosoft: true));

        await LoadedAsync();

        Assert.Empty(_settings.Changes);
        Assert.Empty(await _journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAnimationsAsync_WritesTheSettingAndRecordsWhatItWas()
    {
        var viewModel = await LoadedAsync();

        viewModel.AnimationsEnabled = false;
        await viewModel.ApplyAnimationsAsync(CancellationToken.None);

        Assert.Equal("animations=False", Assert.Single(_settings.Changes));

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));
        var entry = Assert.IsType<SystemSettingChangedEntry>(Assert.Single(session.Entries));
        Assert.Equal("visual-effects-animations", entry.SettingId);
        Assert.Equal("True", entry.PreviousValue);
        Assert.Equal("False", entry.NewValue);
    }

    [Fact]
    public async Task ApplyAnimationsAsync_WindowsRefused_PutsTheSwitchBack()
    {
        _settings.Succeeds = false;
        var viewModel = await LoadedAsync();

        viewModel.AnimationsEnabled = false;
        await viewModel.ApplyAnimationsAsync(CancellationToken.None);

        Assert.True(viewModel.AnimationsEnabled);
        Assert.Contains("PerformanceChangeRefusedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyTransparencyAsync_WritesTheSetting()
    {
        var viewModel = await LoadedAsync();

        viewModel.TransparencyEnabled = false;
        await viewModel.ApplyTransparencyAsync(CancellationToken.None);

        Assert.Equal("transparency=False", Assert.Single(_settings.Changes));
    }

    [Fact]
    public async Task ApplyPowerPlanAsync_ActivatesThePlanAndMarksItCurrent()
    {
        GiveTwoPowerPlans();
        var viewModel = await LoadedAsync();

        viewModel.SelectedPowerPlan = viewModel.PowerPlans.Single(plan => plan.Name == "High performance");
        await viewModel.ApplyPowerPlanAsync(CancellationToken.None);

        Assert.Equal($"power-plan={HighPerformanceId}", Assert.Single(_settings.Changes));
        Assert.True(viewModel.PowerPlans.Single(plan => plan.Id == HighPerformanceId).IsActive);
        Assert.False(viewModel.PowerPlans.Single(plan => plan.Id == BalancedId).IsActive);
    }

    [Fact]
    public async Task ApplyPowerPlanAsync_ThePlanAlreadyRunning_ChangesNothing()
    {
        GiveTwoPowerPlans();
        var viewModel = await LoadedAsync();

        await viewModel.ApplyPowerPlanAsync(CancellationToken.None);

        Assert.Empty(_settings.Changes);
        Assert.Empty(await _journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyIndexingAsync_TurnedOff_DisablesTheWindowsSearchServiceThroughTheHelper()
    {
        // Indexing is a service, so it is a start-type change like any other — and needs admin.
        _scanner.Services.Add(new ServiceInfo("WSearch", "Windows Search", "Automatic", IsRunning: true, IsMicrosoft: true));
        var viewModel = await LoadedAsync();

        viewModel.IndexingEnabled = false;
        await viewModel.ApplyIndexingAsync(CancellationToken.None);

        var request = Assert.IsType<SetServiceStartTypeRequest>(Assert.Single(_elevation.SentRequests));
        Assert.Equal("WSearch", request.ServiceName);
        Assert.Equal(ServiceStartType.Disabled, request.StartType);
    }

    [Fact]
    public async Task ApplyIndexingAsync_TurnedOn_PutsTheServiceBackToDelayedStart()
    {
        _scanner.Services.Add(new ServiceInfo("WSearch", "Windows Search", "Disabled", IsRunning: false, IsMicrosoft: true));
        var viewModel = await LoadedAsync();

        viewModel.IndexingEnabled = true;
        await viewModel.ApplyIndexingAsync(CancellationToken.None);

        var request = Assert.IsType<SetServiceStartTypeRequest>(Assert.Single(_elevation.SentRequests));
        Assert.Equal(ServiceStartType.AutomaticDelayed, request.StartType);
    }

    [Fact]
    public async Task ApplyIndexingAsync_ElevationDeclined_PutsTheSwitchBack()
    {
        _elevation.Availability = new ElevationResult(ElevationStatus.Declined);
        var viewModel = await LoadedAsync();

        viewModel.IndexingEnabled = true;
        await viewModel.ApplyIndexingAsync(CancellationToken.None);

        Assert.False(viewModel.IndexingEnabled);
        Assert.Empty(_elevation.SentRequests);
        Assert.Equal("CommonElevationDeclined", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ApplyIndexingAsync_HelperRefused_PutsTheSwitchBack()
    {
        _elevation.ServiceChangeSucceeds = false;
        var viewModel = await LoadedAsync();

        viewModel.IndexingEnabled = true;
        await viewModel.ApplyIndexingAsync(CancellationToken.None);

        Assert.False(viewModel.IndexingEnabled);
        Assert.Contains("PerformanceChangeRefusedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenCommands_HandWindowsItsOwnDialogs()
    {
        // Upkeep links to these rather than reimplementing a control panel.
        var viewModel = await LoadedAsync();

        viewModel.OpenAdvancedPerformanceOptions();
        viewModel.OpenPowerSettings();
        viewModel.OpenIndexingOptions();

        Assert.Equal(
            [WindowsUiLauncher.PerformanceOptions, WindowsUiLauncher.PowerSettings, WindowsUiLauncher.IndexingOptionsCommand],
            _launcher.Opened.Select(opened => opened.Target));
        Assert.Equal(WindowsUiLauncher.IndexingOptionsArguments, _launcher.Opened[2].Arguments);
    }

    [Fact]
    public async Task OpenCommands_WindowsWouldNotOpenIt_SaysSo()
    {
        _launcher.Succeeds = false;
        var viewModel = await LoadedAsync();

        viewModel.OpenPowerSettings();

        Assert.Equal("PerformanceCouldNotOpenWindows", viewModel.StatusMessage);
    }

    [Fact]
    public async Task EndSessionAsync_ClosesTheSessionForHistory()
    {
        var viewModel = await LoadedAsync();
        viewModel.AnimationsEnabled = false;
        await viewModel.ApplyAnimationsAsync(CancellationToken.None);

        await viewModel.EndSessionAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task ApplyAnimationsAsync_Succeeded_MarksTheEntryCompletedSoItCanBePutBack()
    {
        // A revert skips entries that never completed, so an unstamped entry is a change History
        // records and can never undo.
        var viewModel = await LoadedAsync();

        viewModel.AnimationsEnabled = false;
        await viewModel.ApplyAnimationsAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));

        Assert.True(Assert.Single(session.Entries).Completed);
    }

    [Fact]
    public async Task ApplyAnimationsAsync_WindowsRefused_LeavesTheEntryUnfinished()
    {
        _settings.Succeeds = false;
        var viewModel = await LoadedAsync();

        viewModel.AnimationsEnabled = false;
        await viewModel.ApplyAnimationsAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));

        Assert.False(Assert.Single(session.Entries).Completed);
    }

    [Fact]
    public async Task ApplyPowerPlanAsync_Succeeded_MarksTheEntryCompleted()
    {
        GiveTwoPowerPlans();
        var viewModel = await LoadedAsync();

        viewModel.SelectedPowerPlan = viewModel.PowerPlans.First(plan => plan.Id == HighPerformanceId);
        await viewModel.ApplyPowerPlanAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));

        Assert.True(Assert.Single(session.Entries).Completed);
    }

    [Fact]
    public async Task ApplyIndexingAsync_Succeeded_MarksTheEntryCompleted()
    {
        var viewModel = await LoadedAsync();

        viewModel.IndexingEnabled = true;
        await viewModel.ApplyIndexingAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));

        Assert.True(Assert.Single(session.Entries).Completed);
    }

    [Fact]
    public async Task ApplyIndexingAsync_WindowsRefused_LeavesTheEntryUnfinished()
    {
        _elevation.ServiceChangeSucceeds = false;
        var viewModel = await LoadedAsync();

        viewModel.IndexingEnabled = true;
        await viewModel.ApplyIndexingAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));

        Assert.False(Assert.Single(session.Entries).Completed);
    }
}
