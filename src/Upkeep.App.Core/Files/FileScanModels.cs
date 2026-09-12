namespace Upkeep.App.Core.Files;

/// <summary>Where to look, and what to leave alone. Shared by every scan on the Files page.</summary>
public sealed record FileScanScope
{
    /// <summary>Folders to search. Empty means nothing is scanned — there is no implicit "whole PC".</summary>
    public required IReadOnlyList<string> Roots { get; init; }

    /// <summary>Folders to skip, in addition to the ones Upkeep always skips.</summary>
    public IReadOnlyList<string> Exclusions { get; init; } = [];

    /// <summary>
    /// Files smaller than this are ignored. Duplicated small files are mostly app data and noise:
    /// hundreds of identical 2 KB icons are not what anyone came to the page to find.
    /// </summary>
    public long MinimumFileBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Folders no file scan ever enters, whatever the user picked. These hold files that belong to
    /// Windows or to an app's own state, where "duplicate" means "both are needed".
    /// </summary>
    public static readonly IReadOnlyList<string> AlwaysExcludedFolderNames =
    [
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "AppData",
        "$Recycle.Bin",
        "System Volume Information",
        "WindowsApps",
        "node_modules",
        ".git",
    ];
}

/// <summary>One file a scan is considering, with what the UI needs to show about it.</summary>
public sealed record ScannedFile(string Path, long SizeBytes, DateTime LastWriteUtc)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// A set of files with identical content. Exactly one is marked to keep — the rest are what the
/// page offers to quarantine.
/// </summary>
public sealed record DuplicateGroup(IReadOnlyList<ScannedFile> Files, ScannedFile Keep)
{
    /// <summary>Size of one copy — what every extra copy costs.</summary>
    public long SizeBytes => Keep.SizeBytes;

    public int CopyCount => Files.Count;

    /// <summary>Space that would come back if every copy but one went.</summary>
    public long ReclaimableBytes => SizeBytes * (Files.Count - 1);

    public IEnumerable<ScannedFile> Extras => Files.Where(file => file.Path != Keep.Path);
}

/// <summary>What a duplicate scan found, and what it had to leave out.</summary>
public sealed record DuplicateScanResult(IReadOnlyList<DuplicateGroup> Groups, int FilesExamined, int SkippedCloudFiles)
{
    public long ReclaimableBytes => Groups.Sum(group => group.ReclaimableBytes);

    public static readonly DuplicateScanResult Empty = new([], 0, 0);
}

/// <summary>Progress while a scan runs: these can take minutes on a full drive.</summary>
public sealed record FileScanProgress(string CurrentFolder, int FilesExamined);
