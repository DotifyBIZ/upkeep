using Upkeep.App.Core.Sessions;
using Upkeep.App.Core.Startup;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class StartupViewModelTests : IDisposable
{
    private readonly string _journalDirectory = Path.Combine(Path.GetTempPath(), $"upkeep-startup-vm-{Guid.NewGuid():N}");
    private readonly FakeStartupItemScanner _scanner = new();
    private readonly FakeStartupItemToggler _toggler = new();
    private readonly SessionJournal _journal;

    public StartupViewModelTests() => _journal = new SessionJournal(_journalDirectory);

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

    private StartupViewModel CreateViewModel() =>
        new(_scanner, _toggler, _journal, new FakeLocalizationService(), new FakeAppLogger());

    private static StartupItem Item(
        string id = "Discord",
        StartupSource source = StartupSource.RunKeyCurrentUser,
        bool isEnabled = true,
        bool requiresElevation = false) => new()
        {
            Id = id,
            Name = id,
            Publisher = "Discord Inc.",
            Command = @"C:\Users\a\AppData\Local\Discord\Update.exe",
            Source = source,
            IsEnabled = isEnabled,
            RequiresElevation = requiresElevation,
        };

    [Fact]
    public async Task LoadAsync_ShowsEveryStartupItem()
    {
        _scanner.Items.Add(Item());
        _scanner.Items.Add(Item("OneDrive", StartupSource.RunKeyAllUsers, requiresElevation: true));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.Items[1].RequiresElevation);
    }

    [Fact]
    public async Task LoadAsync_LabelsWhereEachItemStartsFrom()
    {
        _scanner.Items.Add(Item(source: StartupSource.ScheduledTask));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("StartupSourceScheduledTask", viewModel.Items[0].SourceLabel);
    }

    [Fact]
    public async Task LoadAsync_NothingStartsWithWindows_SaysSo()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_RunOnceEntry_CannotBeToggled()
    {
        // It deletes itself after running; a switch would promise something that isn't true.
        _scanner.Items.Add(Item("Finish setup", StartupSource.RunOnce));
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Items[0].CanToggle);
    }

    [Fact]
    public async Task LoadAsync_DisabledItem_ShowsWhenItWasTurnedOff()
    {
        _scanner.Items.Add(Item(isEnabled: false) with { DisabledAt = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero) });
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.Items[0].IsEnabled);
        Assert.Contains("StartupDisabledAtFormat", viewModel.Items[0].DisabledAtDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_PassesTheChangeThroughAndOpensASession()
    {
        _scanner.Items.Add(Item());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        Assert.Equal(("Discord", false), _toggler.Changes.Single());
        Assert.Single(await _journal.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_WindowsRefused_PutsTheSwitchBack()
    {
        // A switch showing a state that never took effect is worse than an error message.
        _scanner.Items.Add(Item());
        _toggler.Succeeds = false;
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        viewModel.Items[0].IsEnabled = false;
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        Assert.True(viewModel.Items[0].IsEnabled);
        Assert.Contains("StartupChangeRefusedFormat", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_SeveralChanges_ShareOneSession()
    {
        // One visit to the page is one session, so History can revert the visit as a whole.
        _scanner.Items.Add(Item());
        _scanner.Items.Add(Item("Steam"));
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);
        await viewModel.ApplyAsync(viewModel.Items[1], CancellationToken.None);

        Assert.Single(await _journal.ListAsync(CancellationToken.None));
        Assert.Equal(2, _toggler.Changes.Count);
    }

    [Fact]
    public async Task EndSessionAsync_ClosesTheSessionForHistory()
    {
        _scanner.Items.Add(Item());
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.ApplyAsync(viewModel.Items[0], CancellationToken.None);

        await viewModel.EndSessionAsync(CancellationToken.None);

        var session = Assert.Single(await _journal.ListAsync(CancellationToken.None));
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task EndSessionAsync_NothingWasChanged_WritesNoSession()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.EndSessionAsync(CancellationToken.None);

        Assert.Empty(await _journal.ListAsync(CancellationToken.None));
    }
}
