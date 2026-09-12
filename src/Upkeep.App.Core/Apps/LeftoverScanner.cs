using System.Text;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Apps;

/// <summary>What kind of remnant an item is.</summary>
public enum LeftoverKind
{
    Folder,
    RegistryKey,
}

/// <summary>One thing an uninstalled app appears to have left behind.</summary>
public sealed record LeftoverItem(LeftoverKind Kind, string Path, long SizeBytes = 0, bool RequiresElevation = false)
{
    /// <summary>Hive for registry items; ignored for folders.</summary>
    public RegistryHiveName Hive { get; init; }
}

/// <summary>
/// Finds what one specific app left behind after its own uninstaller ran.
/// <para>
/// Scoped deliberately narrowly (README, "no general registry cleaner"): it only looks in the
/// handful of places an installer puts things, only matches names derived from *this* app, and
/// refuses anything shared, anything belonging to Windows, and anything too close to the root of a
/// well-known folder. It would rather miss a leftover than remove something another app needs.
/// </para>
/// </summary>
public sealed class LeftoverScanner
{
    /// <summary>
    /// Names that are never one app's leftovers wherever they appear in a path: Windows' own
    /// folders, and vendor folders shared by everything that vendor ships. A path with one of
    /// these anywhere above it belongs to more than the app being uninstalled.
    /// </summary>
    private static readonly HashSet<string> SharedOwnerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "WindowsApps", "Common Files", "Package Cache",
        "Google", "Intel", "NVIDIA Corporation", "AMD", "Realtek", "Apple", "Adobe",
        "Classes", "Policies", "CurrentVersion", "WOW6432Node",
    };

    /// <summary>
    /// Container names that are fine to have *above* a leftover but are never the leftover itself.
    /// <c>%LocalAppData%\Programs\SomeApp</c> is exactly where per-user installers put things, so
    /// "Programs" has to be walked through rather than refused outright — but "Programs" itself is
    /// never one app's folder.
    /// </summary>
    private static readonly HashSet<string> ContainerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Programs", "Temp", "Local", "LocalLow", "Roaming", "Application Data",
        "Users", "Program Files", "Program Files (x86)", "ProgramData", "Software", "AppData",
    };

    /// <summary>Words that say nothing about which app this is, and only get in the way of matching.</summary>
    private static readonly string[] NoiseWords =
    [
        "x64", "x86", "64", "32", "bit", "edition", "version",
        "setup", "installer", "app", "application", "inc", "llc", "ltd", "corporation", "corp", "gmbh",
    ];

    private readonly IWellKnownPaths _paths;
    private readonly IRegistryProbe _registry;

    public LeftoverScanner(IWellKnownPaths paths, IRegistryProbe registry)
    {
        _paths = paths;
        _registry = registry;
    }

    /// <summary>
    /// Everything that looks like it belongs to <paramref name="app"/> and is still there. The
    /// caller shows this as a preview; nothing is removed until the user says so.
    /// </summary>
    public IReadOnlyList<LeftoverItem> Scan(InstalledApp app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var items = new List<LeftoverItem>();
        string? name = Normalize(app.DisplayName);
        string? publisher = Normalize(app.Publisher);

        if (string.IsNullOrEmpty(name))
        {
            return items;
        }

        AddFolderLeftovers(app, name, publisher, items, cancellationToken);
        AddRegistryLeftovers(app, name, publisher, items);

        return items;
    }

    private void AddFolderLeftovers(InstalledApp app, string name, string? publisher, List<LeftoverItem> items, CancellationToken cancellationToken)
    {
        // The app's own stated install location first — the one place it told Windows about.
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation) && IsRemovableFolder(app.InstallLocation))
        {
            AddFolder(app.InstallLocation, items, cancellationToken);
        }

        string[] roots = [_paths.LocalAppData, _paths.RoamingAppData, _paths.ProgramData];

        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Installers rarely name a folder exactly what Programs and Features shows — "Zoom
            // Workplace" ships a "Zoom" folder — so the children are enumerated and matched
            // rather than guessed at by exact name.
            foreach (string child in SafeEnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string childName = Path.GetFileName(child);

                // The publisher's own folder is walked into, never removed: other products from
                // the same vendor live under it. Same rule the registry side applies.
                bool isPublisherFolder = publisher is { Length: >= 3 } && Normalize(childName) == publisher;
                bool isContainer = ContainerNames.Contains(childName);

                if (!isPublisherFolder && !isContainer)
                {
                    if (MatchesApp(childName, name, publisher) && IsRemovableFolder(child))
                    {
                        AddFolder(child, items, cancellationToken);
                    }

                    continue;
                }

                foreach (string grandchild in SafeEnumerateDirectories(child))
                {
                    if (MatchesApp(Path.GetFileName(grandchild), name, publisher) && IsRemovableFolder(grandchild))
                    {
                        AddFolder(grandchild, items, cancellationToken);
                    }
                }
            }
        }
    }

    private void AddFolder(string path, List<LeftoverItem> items, CancellationToken cancellationToken)
    {
        if (items.Any(item => item.Kind == LeftoverKind.Folder && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        items.Add(new LeftoverItem(
            LeftoverKind.Folder,
            path,
            MeasureFolder(path, cancellationToken),
            RequiresElevation(path)));
    }

    private void AddRegistryLeftovers(InstalledApp app, string name, string? publisher, List<LeftoverItem> items)
    {
        (RegistryHiveName Hive, string Path)[] candidates =
        [
            (RegistryHiveName.CurrentUser, $@"Software\{app.DisplayName}"),
            (RegistryHiveName.LocalMachine, $@"SOFTWARE\{app.DisplayName}"),
        ];

        foreach ((var hive, string keyPath) in candidates)
        {
            if (IsRemovableKey(keyPath) && MatchesApp(LastSegment(keyPath), name, publisher) && _registry.KeyExists(hive, keyPath))
            {
                items.Add(new LeftoverItem(LeftoverKind.RegistryKey, keyPath, RequiresElevation: hive == RegistryHiveName.LocalMachine)
                {
                    Hive = hive,
                });
            }
        }

        if (string.IsNullOrEmpty(app.Publisher))
        {
            return;
        }

        // <Hive>\Software\<Publisher>\<App>: remove the app's key, never the publisher's — other
        // products from the same vendor live under it.
        (RegistryHiveName Hive, string Path)[] publisherScoped =
        [
            (RegistryHiveName.CurrentUser, $@"Software\{app.Publisher}\{app.DisplayName}"),
            (RegistryHiveName.LocalMachine, $@"SOFTWARE\{app.Publisher}\{app.DisplayName}"),
        ];

        foreach ((var hive, string keyPath) in publisherScoped)
        {
            if (IsRemovableKey(keyPath) && _registry.KeyExists(hive, keyPath))
            {
                items.Add(new LeftoverItem(LeftoverKind.RegistryKey, keyPath, RequiresElevation: hive == RegistryHiveName.LocalMachine)
                {
                    Hive = hive,
                });
            }
        }
    }

    /// <summary>
    /// Whether a folder is ever a candidate: it must sit under a known root, be at least one level
    /// in, not be a shared owner's folder at any level, and not itself be a plain container. This
    /// is the rule that stops a badly-named app from nominating %LocalAppData% — or Microsoft's
    /// folder inside it.
    /// </summary>
    public bool IsRemovableFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        string[] allowedRoots =
        [
            _paths.LocalAppData,
            _paths.RoamingAppData,
            _paths.ProgramData,
            Path.Combine(_paths.SystemDriveRoot, "Program Files"),
            Path.Combine(_paths.SystemDriveRoot, "Program Files (x86)"),
        ];

        string? containingRoot = allowedRoots.FirstOrDefault(root =>
            full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        if (containingRoot is null)
        {
            return false;
        }

        string relative = full[(Path.TrimEndingDirectorySeparator(containingRoot).Length + 1)..];
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        // Never the root itself, and never buried so deep it belongs to another structure.
        if (segments.Length is 0 or > 3)
        {
            return false;
        }

        // A shared owner anywhere above means the folder isn't this app's alone.
        if (segments.Any(SharedOwnerNames.Contains))
        {
            return false;
        }

        // A container is a place apps live, not an app's own folder.
        return !ContainerNames.Contains(segments[^1]);
    }

    /// <summary>Whether a registry path is ever a candidate — same idea, for keys.</summary>
    public static bool IsRemovableKey(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        var segments = keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        // "Software\X" at minimum, and never deeper than "Software\Publisher\App".
        if (segments.Length is < 2 or > 3)
        {
            return false;
        }

        if (!segments[0].Equals("Software", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !segments.Skip(1).Any(segment => SharedOwnerNames.Contains(segment) || ContainerNames.Contains(segment));
    }

    /// <summary>
    /// Whether a folder or key name plausibly belongs to this app. Compared on normalized names,
    /// so "Zoom Workplace" matches a "Zoom" folder but not a "Zoho" one.
    /// </summary>
    public static bool MatchesApp(string candidateName, string normalizedAppName, string? normalizedPublisher)
    {
        string candidate = Normalize(candidateName) ?? string.Empty;
        if (candidate.Length < 3)
        {
            // Two-character folder names match far too much to be safe.
            return false;
        }

        if (candidate == normalizedAppName)
        {
            return true;
        }

        // One contained in the other: "zoom" under an app called "zoomworkplace", or vice versa.
        if (normalizedAppName.Contains(candidate, StringComparison.Ordinal)
            || candidate.Contains(normalizedAppName, StringComparison.Ordinal))
        {
            return true;
        }

        return normalizedPublisher is { Length: >= 3 } && candidate == normalizedPublisher;
    }

    /// <summary>
    /// Reduces a display name to something comparable: lower case, letters and digits only, with
    /// noise words removed and everything from the version number onwards dropped.
    /// <para>
    /// Digits that are part of the name survive — "7-Zip" is "7zip", not "zip" — because the
    /// version is what follows the name, not every digit in it.
    /// </para>
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(words.Length);

        foreach (string word in words)
        {
            // An all-digit word after the first one starts the version ("Blender 4 5", "7 Zip 24 09").
            if (kept.Count > 0 && word.All(char.IsDigit))
            {
                break;
            }

            if (!NoiseWords.Contains(word, StringComparer.Ordinal))
            {
                kept.Add(word);
            }
        }

        return string.Concat(kept);
    }

    private static string LastSegment(string keyPath) => keyPath.Split('\\')[^1];

    private bool RequiresElevation(string path) =>
        path.StartsWith(_paths.ProgramData, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.Combine(_paths.SystemDriveRoot, "Program Files"), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long MeasureFolder(string path, CancellationToken cancellationToken)
    {
        try
        {
            long total = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += file.Length;
            }

            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
