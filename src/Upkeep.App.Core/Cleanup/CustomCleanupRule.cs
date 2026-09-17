using System.Diagnostics.CodeAnalysis;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Cleanup;

/// <summary>Why a rule the user typed was not accepted. Each value has its own message in the UI.</summary>
public enum CustomRuleProblem
{
    None,

    /// <summary>Nothing was typed.</summary>
    Empty,

    /// <summary>A wildcard in the folder part ("C:\*\cache"), which would make the scope of the
    /// rule impossible to see before it runs.</summary>
    WildcardInFolder,

    /// <summary>A relative path — there is no "current folder" a maintenance tool should guess at.</summary>
    NotAbsolute,

    /// <summary>The folder isn't there.</summary>
    FolderMissing,

    /// <summary>Windows' own files, other people's profiles, or Upkeep's own data.</summary>
    OutsideAllowedArea,

    /// <summary>A whole drive with no filter — every file on it, which is not a cleanup rule.</summary>
    TooBroad,

    /// <summary>The same rule is already in the list.</summary>
    Duplicate,
}

/// <summary>
/// A folder, and which files in it the user considers junk: "D:\Renders\*.cache", or a bare
/// "*.bak", which means that pattern inside their own profile.
/// <para>
/// Everything a rule matches is quarantined, never deleted outright, and appears in the same
/// preview as every built-in category before anything happens to it (ADR-0006). The validation
/// below is what keeps a rule from being pointed at Windows itself: a user's own files are theirs
/// to clean, but a wildcard loose in C:\Windows is how a maintenance tool breaks a machine it
/// cannot see.
/// </para>
/// </summary>
public sealed record CustomCleanupRule(string Root, string Pattern)
{
    /// <summary>What the rule reads as in the UI, and what is persisted in the settings file.</summary>
    public string Display => Path.Combine(Root, Pattern);

    /// <summary>
    /// Parses what the user typed into a rule, or says why it was refused.
    /// </summary>
    /// <param name="input">A folder, a folder plus a pattern, or a bare pattern.</param>
    /// <param name="paths">Where the profile and the system drive are, so tests never depend on
    /// the machine running them.</param>
    public static bool TryParse(
        string? input,
        IWellKnownPaths paths,
        [NotNullWhen(true)] out CustomCleanupRule? rule,
        out CustomRuleProblem problem)
    {
        ArgumentNullException.ThrowIfNull(paths);

        rule = null;
        string trimmed = (input ?? string.Empty).Trim().Trim('"');

        if (trimmed.Length == 0)
        {
            problem = CustomRuleProblem.Empty;
            return false;
        }

        // A bare pattern means "anywhere in my own files", which is the only place Upkeep is
        // willing to guess about.
        bool hasFolder = trimmed.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || trimmed.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

        string root;
        string pattern;

        if (!hasFolder)
        {
            if (!HasWildcard(trimmed))
            {
                // A bare word is neither a folder nor a pattern; refusing it beats quietly turning
                // it into "*name*" and matching more than the user asked for.
                problem = CustomRuleProblem.NotAbsolute;
                return false;
            }

            root = paths.UserProfile;
            pattern = trimmed;
        }
        else
        {
            string lastSegment = Path.GetFileName(trimmed);
            string folderPart = HasWildcard(lastSegment)
                ? Path.GetDirectoryName(trimmed) ?? string.Empty
                : trimmed;

            pattern = HasWildcard(lastSegment) ? lastSegment : "*";

            if (HasWildcard(folderPart))
            {
                problem = CustomRuleProblem.WildcardInFolder;
                return false;
            }

            if (!Path.IsPathFullyQualified(folderPart))
            {
                problem = CustomRuleProblem.NotAbsolute;
                return false;
            }

            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPart));
        }

        if (!Directory.Exists(root))
        {
            problem = CustomRuleProblem.FolderMissing;
            return false;
        }

        if (!IsAllowed(root, paths))
        {
            problem = CustomRuleProblem.OutsideAllowedArea;
            return false;
        }

        if (pattern == "*" && IsDriveRoot(root))
        {
            problem = CustomRuleProblem.TooBroad;
            return false;
        }

        rule = new CustomCleanupRule(root, pattern);
        problem = CustomRuleProblem.None;
        return true;
    }

    /// <summary>
    /// Reads back the rules saved in settings, dropping any that no longer parse — a folder that
    /// has since been removed, or a file someone hand-edited.
    /// </summary>
    public static IReadOnlyList<CustomCleanupRule> ParseAll(IEnumerable<string> saved, IWellKnownPaths paths)
    {
        ArgumentNullException.ThrowIfNull(saved);

        var rules = new List<CustomCleanupRule>();
        foreach (string entry in saved)
        {
            if (TryParse(entry, paths, out var rule, out _) && !rules.Contains(rule))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    private static bool HasWildcard(string value) =>
        value.Contains('*', StringComparison.Ordinal) || value.Contains('?', StringComparison.Ordinal);

    private static bool IsDriveRoot(string path) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(path),
            Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Your own files, or another drive entirely. That leaves out Windows, Program Files, other
    /// people's profiles and the system drive's root — everywhere a wildcard does damage that
    /// quarantine doesn't really undo, because the machine is already broken by then.
    /// </summary>
    private static bool IsAllowed(string root, IWellKnownPaths paths)
    {
        // Upkeep's own folder holds the quarantine and the session journals: a rule that matched
        // inside it would quarantine the record of what it quarantined.
        if (IsUnder(root, Path.Combine(paths.LocalAppData, "Upkeep")))
        {
            return false;
        }

        if (IsUnder(root, paths.UserProfile))
        {
            return true;
        }

        return !IsUnder(root, paths.SystemDriveRoot);
    }

    private static bool IsUnder(string candidate, string folder)
    {
        string normalizedFolder = Path.GetFullPath(folder);
        string normalizedCandidate = Path.GetFullPath(candidate);

        if (Path.TrimEndingDirectorySeparator(normalizedCandidate)
            .Equals(Path.TrimEndingDirectorySeparator(normalizedFolder), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A drive root already ends in a separator, and Path.TrimEndingDirectorySeparator leaves it
        // there. Appending another one produced "C:\\", which nothing starts with — so every path
        // on the system drive read as "not on the system drive", and a rule aimed at C:\Windows was
        // accepted. Found by pointing the real app at C:\Windows\*.dll.
        string prefix = normalizedFolder.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedFolder
            : normalizedFolder + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
