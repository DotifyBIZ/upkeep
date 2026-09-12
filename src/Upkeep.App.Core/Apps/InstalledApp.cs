namespace Upkeep.App.Core.Apps;

/// <summary>Where an installed app came from, which decides how it is removed.</summary>
public enum AppSource
{
    /// <summary>A classic installer, listed under the registry's Uninstall keys.</summary>
    Desktop,

    /// <summary>An MSIX/Store package, removed through the package manager.</summary>
    Store,
}

/// <summary>Whose machine the app is installed for.</summary>
public enum AppScope
{
    /// <summary>Installed for the signed-in user only (HKCU). No elevation to remove.</summary>
    CurrentUser,

    /// <summary>Installed for everyone (HKLM). Its own uninstaller will ask for elevation.</summary>
    AllUsers,
}

/// <summary>
/// One installed app, as shown in the uninstaller list.
/// <para>
/// Upkeep never removes an app itself: it runs the app's own uninstaller (or asks the package
/// manager to remove a Store app), and only afterwards offers to clear what that app left behind.
/// Everything here is what it takes to do that, and what the user needs to recognize the entry.
/// </para>
/// </summary>
public sealed record InstalledApp
{
    /// <summary>Stable identifier: the registry key name, or the package full name for Store apps.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    /// <summary>Where the app installed itself, when it said — used to look for leftovers.</summary>
    public string? InstallLocation { get; init; }

    /// <summary>What the registry claims the install weighs. Often missing, and often wrong, so
    /// the UI shows it as an estimate rather than a measurement.</summary>
    public long? EstimatedBytes { get; init; }

    public DateOnly? InstalledOn { get; init; }

    public required AppSource Source { get; init; }

    public required AppScope Scope { get; init; }

    /// <summary>The app's own uninstall command, parsed. Null for Store apps.</summary>
    public UninstallCommand? Uninstall { get; init; }

    /// <summary>The registry key this entry came from, for the leftover scan.</summary>
    public string? RegistryKeyPath { get; init; }

    /// <summary>The MSIX package full name, for Store apps.</summary>
    public string? PackageFullName { get; init; }
}
