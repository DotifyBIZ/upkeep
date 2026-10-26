using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Performance;

/// <summary>What Settings shows under "Let this app run in background".</summary>
public enum BackgroundAccess
{
    /// <summary>Windows decides — its default, shown as "Power optimized".</summary>
    PowerOptimized,
    Always,
    Never,
}

/// <summary>One Store app and whether Windows lets it run in the background.</summary>
public sealed record BackgroundApp(string PackageFamilyName, string DisplayName, BackgroundAccess Access);

/// <summary>
/// The values Settings writes under BackgroundAccessApplications (ADR-0010). Undocumented, so the
/// whole format lives here and nowhere else.
/// </summary>
public static partial class BackgroundAccessValues
{
    public const string RootKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";

    public const string Disabled = "Disabled";
    public const string DisabledByUser = "DisabledByUser";
    public const string IgnoreBatterySaver = "IgnoreBatterySaver";
    public const string SleepDisabled = "SleepDisabled";

    /// <summary>Every value Upkeep reads, writes or puts back for one app. Nothing else is touched.</summary>
    public static readonly IReadOnlyList<string> Managed = [Disabled, DisabledByUser, IgnoreBatterySaver, SleepDisabled];

    public static BackgroundAccess Classify(int? disabled, int? disabledByUser, int? ignoreBatterySaver)
    {
        if (disabled == 1 || disabledByUser == 1)
        {
            return BackgroundAccess.Never;
        }

        return ignoreBatterySaver == 1 ? BackgroundAccess.Always : BackgroundAccess.PowerOptimized;
    }

    /// <summary>
    /// A package family name is <c>Name_PublisherId</c>, the publisher id being 13 base-32
    /// characters. Anything else — including a name read back from a session manifest someone
    /// edited — is refused before it becomes part of a registry path.
    /// </summary>
    public static bool IsValidFamilyName(string? familyName) =>
        familyName is not null && FamilyNamePattern().IsMatch(familyName);

    public static string KeyFor(string familyName) => RootKey + "\\" + familyName;

    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,100}_[a-z0-9]{13}$", RegexOptions.CultureInvariant)]
    private static partial Regex FamilyNamePattern();
}

/// <summary>The Store apps a person would recognise: installed for them, with a name, not a framework.</summary>
public interface IStorePackageCatalog
{
    /// <summary>Display name by package family name.</summary>
    IReadOnlyDictionary<string, string> UserFacingPackages();
}

/// <summary>Reads and changes which Store apps may run in the background.</summary>
public interface IBackgroundAppSettings
{
    IReadOnlyList<BackgroundApp> Read();

    /// <summary>What the app's values are now — journaled before any change, so a revert is exact.</summary>
    IReadOnlyList<RegistryValueSnapshot> Snapshot(string familyName);

    /// <summary>Never lets it run in the background (off), or hands the decision back to Windows (on).</summary>
    bool SetAllowed(string familyName, bool allowed, out string? failure);

    /// <summary>Puts an app's values back exactly as a snapshot recorded them.</summary>
    bool Restore(string familyName, IReadOnlyList<RegistryValueSnapshot> previous, out string? failure);
}

/// <inheritdoc />
public sealed class BackgroundAppSettings : IBackgroundAppSettings
{
    private readonly IRegistryProbe _registry;
    private readonly IStorePackageCatalog _packages;

    public BackgroundAppSettings(IRegistryProbe registry, IStorePackageCatalog packages)
    {
        _registry = registry;
        _packages = packages;
    }

    public IReadOnlyList<BackgroundApp> Read()
    {
        var names = _packages.UserFacingPackages();

        return [.. _registry.GetSubKeyNames(RegistryHiveName.CurrentUser, BackgroundAccessValues.RootKey)
            .Where(BackgroundAccessValues.IsValidFamilyName)
            .Where(names.ContainsKey)
            .Select(familyName => new BackgroundApp(familyName, names[familyName], ReadAccess(familyName)))
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public IReadOnlyList<RegistryValueSnapshot> Snapshot(string familyName)
    {
        RequireValid(familyName);

        return [.. _registry.GetValues(RegistryHiveName.CurrentUser, BackgroundAccessValues.KeyFor(familyName))
            .Where(value => BackgroundAccessValues.Managed.Contains(value.Name, StringComparer.OrdinalIgnoreCase))];
    }

    public bool SetAllowed(string familyName, bool allowed, out string? failure)
    {
        RequireValid(familyName);
        string key = BackgroundAccessValues.KeyFor(familyName);

        if (!ClearManaged(key, out failure))
        {
            return false;
        }

        if (allowed)
        {
            // No values at all is Windows' default: "Power optimized".
            return true;
        }

        return _registry.SetCurrentUserValue(key, Dword(BackgroundAccessValues.Disabled, 1), out failure)
            && _registry.SetCurrentUserValue(key, Dword(BackgroundAccessValues.DisabledByUser, 1), out failure);
    }

    public bool Restore(string familyName, IReadOnlyList<RegistryValueSnapshot> previous, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(previous);
        RequireValid(familyName);
        string key = BackgroundAccessValues.KeyFor(familyName);

        if (!ClearManaged(key, out failure))
        {
            return false;
        }

        foreach (var value in previous)
        {
            // A manifest is input like any other: only the values this class manages go back.
            if (!BackgroundAccessValues.Managed.Contains(value.Name, StringComparer.OrdinalIgnoreCase))
            {
                failure = $"{value.Name} is not a background-access value.";
                return false;
            }

            if (!_registry.SetCurrentUserValue(key, value, out failure))
            {
                return false;
            }
        }

        return true;
    }

    private BackgroundAccess ReadAccess(string familyName)
    {
        string key = BackgroundAccessValues.KeyFor(familyName);
        return BackgroundAccessValues.Classify(
            _registry.GetInt32Value(RegistryHiveName.CurrentUser, key, BackgroundAccessValues.Disabled),
            _registry.GetInt32Value(RegistryHiveName.CurrentUser, key, BackgroundAccessValues.DisabledByUser),
            _registry.GetInt32Value(RegistryHiveName.CurrentUser, key, BackgroundAccessValues.IgnoreBatterySaver));
    }

    private bool ClearManaged(string key, out string? failure)
    {
        var present = _registry.GetValueNames(RegistryHiveName.CurrentUser, key);
        foreach (string name in BackgroundAccessValues.Managed)
        {
            if (present.Contains(name, StringComparer.OrdinalIgnoreCase) && !_registry.DeleteCurrentUserValue(key, name, out failure))
            {
                return false;
            }
        }

        failure = null;
        return true;
    }

    private static RegistryValueSnapshot Dword(string name, long value) => new(name, "DWord", Number: value);

    private static void RequireValid(string familyName)
    {
        if (!BackgroundAccessValues.IsValidFamilyName(familyName))
        {
            throw new ArgumentOutOfRangeException(nameof(familyName), familyName, "Not a package family name.");
        }
    }
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin PackageManager wrapper; which packages count as user-facing is decided by the filter it calls, and BackgroundAppSettings is tested against a substitute.")]
public sealed class StorePackageCatalog : IStorePackageCatalog
{
    public IReadOnlyDictionary<string, string> UserFacingPackages()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var packageManager = new Windows.Management.Deployment.PackageManager();
            foreach (var package in packageManager.FindPackagesForUser(string.Empty))
            {
                try
                {
                    // The same filter as the uninstaller: dependencies and inbox Windows
                    // components aren't apps a person chose, so they aren't offered here either.
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle
                        || package.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System)
                    {
                        continue;
                    }

                    string displayName = package.DisplayName;

                    // Unresolved resource strings ("ms-resource:...") are packages without a name
                    // a person would recognise.
                    if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    {
                        names.TryAdd(package.Id.FamilyName, displayName);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or COMException)
                {
                    // A package mid-update can throw on almost any property; skip it.
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            // No catalog means no list, which is what the page shows — not a failure to report.
        }

        return names;
    }
}
