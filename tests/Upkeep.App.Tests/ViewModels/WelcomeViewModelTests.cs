using CommunityToolkit.Mvvm.Messaging;
using Upkeep.App.Core.Settings;
using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class WelcomeViewModelTests
{
    private readonly FakeAppSettingsService _settings = new();

    private WelcomeViewModel CreateViewModel() => new(_settings, new FakeLocalizationService());

    [Fact]
    public void Constructor_BuildsThreeScreensStartingAtTheFirst()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(3, viewModel.Steps.Count);
        Assert.Equal(0, viewModel.StepIndex);
        Assert.Equal(viewModel.Steps[0], viewModel.Current);
    }

    [Fact]
    public void Constructor_EveryScreenHasAGlyphTitleAndBody()
    {
        var viewModel = CreateViewModel();

        Assert.All(viewModel.Steps, step =>
        {
            Assert.NotEmpty(step.Glyph);
            Assert.NotEmpty(step.Title);
            Assert.NotEmpty(step.Body);
        });
    }

    [Fact]
    public void Next_AdvancesOneScreen()
    {
        var viewModel = CreateViewModel();

        viewModel.Next();

        Assert.Equal(1, viewModel.StepIndex);
        Assert.Equal(viewModel.Steps[1], viewModel.Current);
    }

    [Fact]
    public void Next_OnTheLastScreen_StaysThereRatherThanWrapping()
    {
        var viewModel = CreateViewModel();
        viewModel.Next();
        viewModel.Next();

        viewModel.Next();

        Assert.Equal(2, viewModel.StepIndex);
        Assert.True(viewModel.IsLastStep);
    }

    [Fact]
    public void NextLabel_ChangesOnTheLastScreen()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("WelcomeNextButton", viewModel.NextLabel);

        viewModel.StepIndex = 2;

        Assert.Equal("WelcomeFinishButton", viewModel.NextLabel);
    }

    [Fact]
    public void Reset_GoesBackToTheFirstScreen()
    {
        var viewModel = CreateViewModel();
        viewModel.StepIndex = 2;

        viewModel.Reset();

        Assert.Equal(0, viewModel.StepIndex);
        Assert.False(viewModel.IsLastStep);
    }

    [Fact]
    public async Task ShouldShowOnLaunchAsync_FirstEverLaunch_IsTrue()
    {
        Assert.True(await CreateViewModel().ShouldShowOnLaunchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ShouldShowOnLaunchAsync_AlreadySeen_IsFalse()
    {
        _settings.Settings = new AppSettings { HasSeenWelcome = true };

        Assert.False(await CreateViewModel().ShouldShowOnLaunchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MarkSeenAsync_SavesTheFlag()
    {
        var viewModel = CreateViewModel();

        await viewModel.MarkSeenAsync(CancellationToken.None);

        Assert.True(_settings.Settings.HasSeenWelcome);
        Assert.Single(_settings.Saves);
    }

    [Fact]
    public async Task MarkSeenAsync_AlreadySeen_DoesNotWriteAgain()
    {
        // Replaying the tour from Settings ends the same way as the first run; rewriting an
        // unchanged setting every time is a file write for nothing.
        _settings.Settings = new AppSettings { HasSeenWelcome = true };
        var viewModel = CreateViewModel();

        await viewModel.MarkSeenAsync(CancellationToken.None);

        Assert.Empty(_settings.Saves);
    }

    [Fact]
    public void ReplayCommand_AsksTheShellToShowTheTour()
    {
        // Settings can't open the dialog itself — it lives on MainPage, outside the frame.
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        bool asked = false;
        messenger.Register<ShowWelcomeMessage>(recipient, (_, _) => asked = true);

        new SettingsViewModel(
            _settings,
            new FakeUpdateCheckService(),
            new FakeQuarantineStore(),
            new FakeWindowsUiLauncher(),
            new FakeLocalizationService(),
            new FakeAppLogger(),
            messenger).ReplayWelcomeTourCommand.Execute(null);

        Assert.True(asked);
        GC.KeepAlive(recipient);
    }
}
