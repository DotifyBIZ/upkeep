using System.Globalization;

namespace Upkeep.App.Core.Apps;

/// <summary>An uninstaller to run: an executable and its arguments, never a raw command line.</summary>
public sealed record UninstallCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>The raw values one Uninstall registry key holds, before anything is decided about them.</summary>
public sealed record UninstallEntry
{
    public required string KeyName { get; init; }

    public string? DisplayName { get; init; }

    public string? DisplayVersion { get; init; }

    public string? Publisher { get; init; }

    public string? InstallLocation { get; init; }

    public string? UninstallString { get; init; }

    public string? QuietUninstallString { get; init; }

    /// <summary>Set on entries Windows itself hides from Programs and Features.</summary>
    public int? SystemComponent { get; init; }

    /// <summary>Present on updates and patches rather than apps.</summary>
    public string? ParentKeyName { get; init; }

    public string? ReleaseType { get; init; }

    /// <summary>Kilobytes, as the registry stores it.</summary>
    public int? EstimatedSize { get; init; }

    /// <summary>"yyyyMMdd", as the registry stores it.</summary>
    public string? InstallDate { get; init; }

    public bool? WindowsInstaller { get; init; }
}

/// <summary>
/// Decides which registry entries are apps a person would recognize, and turns an uninstall string
/// into something safe to launch.
/// <para>
/// Pure, and deliberately separate from the registry reading: which entries are shown and what
/// command runs are the decisions worth testing, and neither needs a registry to make.
/// </para>
/// </summary>
public static class UninstallEntryParser
{
    /// <summary>
    /// Whether this entry is a real, user-visible application. Mirrors what Programs and Features
    /// shows: named entries with a way to remove them, minus system components, updates and
    /// patches — listing those would invite someone to uninstall a Windows update by mistake.
    /// </summary>
    public static bool IsUserVisibleApp(UninstallEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return false;
        }

        if (entry.SystemComponent == 1)
        {
            return false;
        }

        // An entry that belongs to another product is an update to it, not an app of its own.
        if (!string.IsNullOrEmpty(entry.ParentKeyName))
        {
            return false;
        }

        if (entry.ReleaseType is "Update" or "Hotfix" or "Security Update" or "ServicePack")
        {
            return false;
        }

        // Nothing to run means nothing Upkeep can do beyond listing it, which is worse than not
        // listing it: a row whose only button does nothing.
        return !string.IsNullOrWhiteSpace(entry.UninstallString) || !string.IsNullOrWhiteSpace(entry.QuietUninstallString);
    }

    /// <summary>
    /// Splits an uninstall string into an executable and arguments.
    /// <para>
    /// MSI entries are rewritten from <c>/I</c> (install/modify) to <c>/X</c> (uninstall): the
    /// registry stores the former for Windows Installer products, and running it as-is opens a
    /// repair dialog rather than removing anything.
    /// </para>
    /// </summary>
    public static UninstallCommand? ParseCommand(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
        {
            return null;
        }

        string trimmed = uninstallString.Trim();
        (string fileName, string remainder) = SplitExecutable(trimmed);

        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var arguments = SplitArguments(remainder);

        if (Path.GetFileNameWithoutExtension(fileName).Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            arguments = [.. arguments.Select(RewriteMsiInstallToUninstall)];
        }

        return new UninstallCommand(fileName, arguments);
    }

    /// <summary>Kilobytes as stored, bytes as everything else in the app uses them.</summary>
    public static long? ToBytes(int? estimatedSizeKilobytes) =>
        estimatedSizeKilobytes is > 0 ? estimatedSizeKilobytes.Value * 1024L : null;

    /// <summary>Parses the registry's "yyyyMMdd" install date, ignoring the many malformed ones.</summary>
    public static DateOnly? ParseInstallDate(string? installDate) =>
        DateOnly.TryParseExact(installDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static (string FileName, string Remainder) SplitExecutable(string command)
    {
        if (command.StartsWith('"'))
        {
            int closing = command.IndexOf('"', 1);
            return closing < 0
                ? (command.Trim('"'), string.Empty)
                : (command[1..closing], command[(closing + 1)..].Trim());
        }

        // Unquoted paths with spaces are ambiguous by nature. Windows resolves them by probing;
        // here the first token wins, which is right for the overwhelmingly common
        // "C:\Windows\System32\msiexec.exe /X{GUID}" shape.
        int space = command.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? (command, string.Empty) : (command[..space], command[(space + 1)..].Trim());
    }

    private static List<string> SplitArguments(string remainder)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return arguments;
        }

        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char character in remainder)
        {
            switch (character)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                    if (current.Length > 0)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                    }

                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private static string RewriteMsiInstallToUninstall(string argument) =>
        argument.Length > 1 && argument[0] is '/' or '-' && argument[1] is 'I' or 'i'
            ? string.Concat("/X", argument.AsSpan(2))
            : argument;
}
