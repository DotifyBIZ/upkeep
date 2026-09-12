using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Apps;

/// <summary>Lists what is installed on this PC.</summary>
public interface IInstalledAppScanner
{
    Task<IReadOnlyList<InstalledApp>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the registry's Uninstall keys (both 64- and 32-bit views, machine-wide and per-user) and
/// the Store's package list, and hands each raw entry to <see cref="UninstallEntryParser"/> to
/// decide whether it is a real app and how it would be removed.
/// <para>
/// A reading adapter only — every judgement lives in the parser, which is tested.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads this machine's registry and package list; the decisions about what it finds live in UninstallEntryParser and LeftoverScanner, which are tested.")]
public sealed class InstalledAppScanner : IInstalledAppScanner
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<IReadOnlyList<InstalledApp>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<InstalledApp>>(
            () =>
            {
                var apps = new List<InstalledApp>();

                // Machine-wide, both registry views: a 32-bit installer on 64-bit Windows lands in
                // WOW6432Node, and half the list is missing if only one view is read.
                ReadUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry64, AppScope.AllUsers, apps, cancellationToken);
                ReadUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry32, AppScope.AllUsers, apps, cancellationToken);
                ReadUninstallKeys(RegistryHive.CurrentUser, RegistryView.Registry64, AppScope.CurrentUser, apps, cancellationToken);

                AddStoreApps(apps, cancellationToken);

                return [.. apps.OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
            },
            cancellationToken);

    private static void ReadUninstallKeys(
        RegistryHive hive,
        RegistryView view,
        AppScope scope,
        List<InstalledApp> apps,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(UninstallKeyPath);
            if (uninstallKey is null)
            {
                return;
            }

            foreach (string keyName in uninstallKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var key = uninstallKey.OpenSubKey(keyName);
                if (key is null)
                {
                    continue;
                }

                var entry = ReadEntry(key, keyName);
                if (!UninstallEntryParser.IsUserVisibleApp(entry))
                {
                    continue;
                }

                // The same app can appear in both registry views; keep the first.
                if (apps.Any(app => string.Equals(app.DisplayName, entry.DisplayName, StringComparison.OrdinalIgnoreCase)
                    && app.Scope == scope))
                {
                    continue;
                }

                apps.Add(new InstalledApp
                {
                    Id = keyName,
                    DisplayName = entry.DisplayName!,
                    Publisher = entry.Publisher,
                    Version = entry.DisplayVersion,
                    InstallLocation = entry.InstallLocation,
                    EstimatedBytes = UninstallEntryParser.ToBytes(entry.EstimatedSize),
                    InstalledOn = UninstallEntryParser.ParseInstallDate(entry.InstallDate),
                    Source = AppSource.Desktop,
                    Scope = scope,
                    Uninstall = UninstallEntryParser.ParseCommand(entry.QuietUninstallString ?? entry.UninstallString),
                    RegistryKeyPath = $@"{UninstallKeyPath}\{keyName}",
                });
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // A hive we can't read is a shorter list, not a failed scan.
        }
    }

    private static UninstallEntry ReadEntry(RegistryKey key, string keyName) => new()
    {
        KeyName = keyName,
        DisplayName = key.GetValue("DisplayName") as string,
        DisplayVersion = key.GetValue("DisplayVersion") as string,
        Publisher = key.GetValue("Publisher") as string,
        InstallLocation = key.GetValue("InstallLocation") as string,
        UninstallString = key.GetValue("UninstallString") as string,
        QuietUninstallString = key.GetValue("QuietUninstallString") as string,
        SystemComponent = key.GetValue("SystemComponent") as int?,
        ParentKeyName = key.GetValue("ParentKeyName") as string,
        ReleaseType = key.GetValue("ReleaseType") as string,
        EstimatedSize = key.GetValue("EstimatedSize") as int?,
        InstallDate = key.GetValue("InstallDate") as string,
        WindowsInstaller = key.GetValue("WindowsInstaller") is int installer && installer == 1,
    };

    /// <summary>
    /// Store apps come from the package manager, which works unpackaged for the current user —
    /// unlike most WinRT APIs this app can't touch (see CLAUDE.md).
    /// </summary>
    private static void AddStoreApps(List<InstalledApp> apps, CancellationToken cancellationToken)
    {
        try
        {
            var packageManager = new Windows.Management.Deployment.PackageManager();

            foreach (var package in packageManager.FindPackagesForUser(string.Empty))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Frameworks and resource packages are dependencies of other apps, not things
                    // a person installed; removing one breaks whatever depends on it.
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
                    {
                        continue;
                    }

                    // Inbox Windows components are removable in theory and a bad idea in practice.
                    if (package.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System)
                    {
                        continue;
                    }

                    apps.Add(new InstalledApp
                    {
                        Id = package.Id.FullName,
                        DisplayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Id.Name : package.DisplayName,
                        Publisher = string.IsNullOrWhiteSpace(package.PublisherDisplayName) ? package.Id.Publisher : package.PublisherDisplayName,
                        Version = $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision}",
                        InstallLocation = null,
                        Source = AppSource.Store,
                        Scope = AppScope.CurrentUser,
                        PackageFullName = package.Id.FullName,
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    // A package mid-update can throw on almost any property; skip it.
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // No package manager available: the desktop list still stands on its own.
        }
    }
}
