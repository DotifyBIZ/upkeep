namespace Upkeep.App.Core.Settings;

/// <summary>App-level preferences, persisted locally and never synced anywhere.</summary>
public sealed class AppSettings
{
    /// <summary>Default on: a single unauthenticated GET to GitHub's public releases API with no
    /// identifying data attached — not telemetry, but still an outbound call, so it stays visible
    /// and disableable. See docs/adr/0004-update-check-network-call.md.</summary>
    public bool CheckForUpdatesEnabled { get; set; } = true;

    /// <summary>Empty means "follow the Windows display language"; otherwise a BCP-47 tag the user
    /// picked in Settings ("en-US", "pl-PL"). Applied at startup, before any window exists.</summary>
    public string LanguageOverride { get; set; } = string.Empty;

    /// <summary>How long quarantined items are kept before they're deleted for good
    /// (docs/adr/0006-safety-model.md). Seven days by product decision.</summary>
    public int QuarantineRetentionDays { get; set; } = 7;

    /// <summary>Set once the welcome tour has been shown, whether it was read through or skipped.
    /// A tool that runs as administrator on someone's own machine gets to explain itself once, not
    /// every launch.</summary>
    public bool HasSeenWelcome { get; set; }

    /// <summary>
    /// The user's own cleanup rules, as they typed them ("D:\Renders\*.cache"). Stored as text and
    /// re-validated on every read: this file is user-writable, and a rule decides what gets
    /// quarantined. See <see cref="Cleanup.CustomCleanupRule"/>.
    /// </summary>
    public List<string> CustomCleanupRules { get; set; } = [];
}
