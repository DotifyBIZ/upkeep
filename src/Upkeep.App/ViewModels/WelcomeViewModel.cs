using CommunityToolkit.Mvvm.ComponentModel;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Settings;

namespace Upkeep.App.ViewModels;

/// <summary>One screen of the welcome tour: a Segoe Fluent glyph and the promise it illustrates.</summary>
public sealed record WelcomeStep(string Glyph, string Title, string Body);

/// <summary>
/// The three screens shown the first time Upkeep runs, and again from Settings on request.
/// <para>
/// They exist because of what this app is allowed to do: delete files, change services and run as
/// administrator on a machine Dotify does not own. The three things worth saying before any of
/// that happens are that nothing leaves the PC, that nothing runs without a preview, and that a
/// session can be undone — in that order, because that is the order someone worries about them.
/// </para>
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly ILocalizationService _localization;

    public WelcomeViewModel(IAppSettingsService settings, ILocalizationService localization)
    {
        _settings = settings;
        _localization = localization;

        // Segoe Fluent Icons: a padlock, an eye, and History's own clock — the same glyph the rail
        // uses for the page that does the undoing.
        Steps =
        [
            new WelcomeStep("\uE72E", localization.GetString("WelcomePrivacyTitle"), localization.GetString("WelcomePrivacyBody")),
            new WelcomeStep("\uE890", localization.GetString("WelcomePreviewTitle"), localization.GetString("WelcomePreviewBody")),
            new WelcomeStep("\uE81C", localization.GetString("WelcomeRevertTitle"), localization.GetString("WelcomeRevertBody")),
        ];
    }

    public IReadOnlyList<WelcomeStep> Steps { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    public partial int StepIndex { get; set; }

    public WelcomeStep Current => Steps[StepIndex];

    public bool IsLastStep => StepIndex == Steps.Count - 1;

    /// <summary>"Next" until the last screen, where the same button starts using the app.</summary>
    public string NextLabel => _localization.GetString(IsLastStep ? "WelcomeFinishButton" : "WelcomeNextButton");

    /// <summary>Back to the first screen — the tour is replayable from Settings.</summary>
    public void Reset() => StepIndex = 0;

    /// <summary>Advances one screen. Stops on the last rather than wrapping.</summary>
    public void Next()
    {
        if (!IsLastStep)
        {
            StepIndex++;
        }
    }

    /// <summary>
    /// Records that the tour has been seen. Skipping counts: someone who dismissed it has made
    /// their decision, and showing it again next launch would be nagging.
    /// </summary>
    public async Task MarkSeenAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        if (settings.HasSeenWelcome)
        {
            return;
        }

        settings.HasSeenWelcome = true;
        await _settings.SaveAsync(settings, cancellationToken);
    }

    /// <summary>Whether this launch is the one that shows the tour.</summary>
    public async Task<bool> ShouldShowOnLaunchAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        return !settings.HasSeenWelcome;
    }
}
