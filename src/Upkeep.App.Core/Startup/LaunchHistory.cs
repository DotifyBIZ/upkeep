using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Startup;

/// <summary>When the person last started something themselves, from Start, the taskbar or Explorer.</summary>
public interface ILaunchHistory
{
    /// <summary>
    /// The last time any launch matching <paramref name="keys"/> was recorded, or null when none
    /// was. Keys are compared after <see cref="LaunchHistoryMatch.Normalize"/>.
    /// </summary>
    DateTimeOffset? LastStartedByUser(IEnumerable<string> keys);
}

/// <summary>
/// Reads Explorer's UserAssist record — undocumented, and described in ADR-0010.
/// <para>
/// It records what the person started through the shell, not what Windows started at sign-in or
/// what was reopened from a tray icon. So it can only ever suggest that something looks unused;
/// it never decides anything on its own.
/// </para>
/// </summary>
public sealed class UserAssistLaunchHistory : ILaunchHistory
{
    /// <summary>Executables, and shortcuts — the two UserAssist lists that name programs.</summary>
    public static readonly IReadOnlyList<string> CountKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count",
    ];

    private readonly IRegistryProbe _registry;
    private Dictionary<string, DateTimeOffset>? _latestByToken;

    public UserAssistLaunchHistory(IRegistryProbe registry) => _registry = registry;

    public DateTimeOffset? LastStartedByUser(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _latestByToken ??= Load();

        DateTimeOffset? latest = null;
        foreach (string key in keys)
        {
            string normalized = LaunchHistoryMatch.Normalize(key);
            if (normalized.Length > 0
                && _latestByToken.TryGetValue(normalized, out DateTimeOffset when)
                && (latest is null || when > latest))
            {
                latest = when;
            }
        }

        return latest;
    }

    private Dictionary<string, DateTimeOffset> Load()
    {
        var latest = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        foreach (string countKey in CountKeys)
        {
            foreach (string valueName in _registry.GetValueNames(RegistryHiveName.CurrentUser, countKey))
            {
                if (UserAssistParser.LastRun(_registry.GetBinaryValue(RegistryHiveName.CurrentUser, countKey, valueName)) is not DateTimeOffset when)
                {
                    continue;
                }

                foreach (string token in LaunchHistoryMatch.Tokens(UserAssistParser.Rot13(valueName)))
                {
                    if (!latest.TryGetValue(token, out DateTimeOffset existing) || when > existing)
                    {
                        latest[token] = when;
                    }
                }
            }
        }

        return latest;
    }
}

/// <summary>The UserAssist value format (ADR-0010): ROT13 names, 72-byte data, last run at offset 60.</summary>
public static class UserAssistParser
{
    private const int LastRunOffset = 60;

    public static string Rot13(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return string.Create(value.Length, value, static (span, source) =>
        {
            for (int index = 0; index < source.Length; index++)
            {
                char c = source[index];
                span[index] = c switch
                {
                    >= 'a' and <= 'z' => (char)('a' + ((c - 'a' + 13) % 26)),
                    >= 'A' and <= 'Z' => (char)('A' + ((c - 'A' + 13) % 26)),
                    _ => c,
                };
            }
        });
    }

    /// <summary>The last run time, or null when the data is too short, empty or nonsense.</summary>
    public static DateTimeOffset? LastRun(byte[]? data)
    {
        if (data is null || data.Length < LastRunOffset + sizeof(long))
        {
            return null;
        }

        long fileTime = BitConverter.ToInt64(data, LastRunOffset);
        if (fileTime <= 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc())
        {
            return null;
        }

        return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero);
    }
}

/// <summary>
/// How a startup item is matched to what the person launched.
/// <para>
/// UserAssist names a launch by path (<c>{known-folder}\Steam\steam.exe</c>) or by app id
/// (<c>Valve.Steam.Client</c>, <c>MSEdge</c>), so matching is by word rather than by path. It is
/// generous on purpose: a wrong match makes an app look used, which only means Upkeep doesn't
/// suggest it. A missed match is the costly mistake, because then an app looks unused.
/// </para>
/// </summary>
public static class LaunchHistoryMatch
{
    /// <summary>Words too common in paths and app ids to say which app a launch was.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "exe", "lnk", "app", "apps", "client", "launcher", "microsoft", "windows", "program", "programs",
        "files", "x86", "bin", "update", "updater", "setup", "start", "menu", "desktop", "inc", "com",
    };

    /// <summary>Lower-case letters and digits only, so <c>Spotify.exe</c> and <c>spotify</c> meet.</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    /// <summary>The words in a launch's name worth matching on.</summary>
    public static IEnumerable<string> Tokens(string launchName)
    {
        ArgumentNullException.ThrowIfNull(launchName);

        string fileStem = Path.GetFileNameWithoutExtension(launchName.Split('\\', '/').Last());
        var words = launchName.Split(['\\', '/', '.', '!', '_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries).Append(fileStem);

        return words
            .Select(Normalize)
            .Where(word => word.Length >= 3 && !Ignored.Contains(word))
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// The keys a startup item is known by: its name in the registry or Startup folder, and the
    /// file name of what it runs.
    /// </summary>
    public static IReadOnlyList<string> KeysFor(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var keys = new List<string> { item.Name };
        if (StartupCommand.ExecutableStem(item.Command) is string stem)
        {
            keys.Add(stem);
        }

        return keys;
    }
}

/// <summary>Picks the program out of a startup command line.</summary>
public static class StartupCommand
{
    /// <summary>
    /// The file name, without extension, of what a command runs — <c>steam</c> for
    /// <c>"C:\Program Files (x86)\Steam\steam.exe" -silent</c>. Null when there is nothing to read.
    /// </summary>
    public static string? ExecutableStem(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string trimmed = command.Trim();
        string path;

        if (trimmed[0] == '"')
        {
            int closing = trimmed.IndexOf('"', 1);
            path = closing > 1 ? trimmed[1..closing] : trimmed.Trim('"');
        }
        else if (trimmed.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            // A Startup-folder item's "command" is the shortcut's own path, spaces and all.
            path = trimmed;
        }
        else
        {
            // Unquoted paths may contain spaces (C:\Program Files\...), so the program ends at the
            // first ".exe" that ends a word, not at the first space.
            int exe = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exe >= 0 && (exe + 4 == trimmed.Length || trimmed[exe + 4] == ' ')
                ? trimmed[..(exe + 4)]
                : trimmed.Split(' ')[0];
        }

        string stem = Path.GetFileNameWithoutExtension(path);
        return stem.Length > 0 ? stem : null;
    }
}
