using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Cleanup;

/// <summary>
/// Scans the categories that live inside the signed-in user's profile. Everything here runs as the
/// user, with no elevation: the paths come from <see cref="IWellKnownPaths"/>, so the same code is
/// exercised against a temporary tree in tests rather than against the machine running them.
/// </summary>
public sealed class JunkScanner : IJunkScanner
{
    private readonly IWellKnownPaths _paths;
    private readonly IRecycleBin _recycleBin;
    private readonly IRunningProcesses _processes;
    private readonly TimeProvider _timeProvider;

    public JunkScanner(IWellKnownPaths paths, IRecycleBin recycleBin, IRunningProcesses processes, TimeProvider? timeProvider = null)
    {
        _paths = paths;
        _recycleBin = recycleBin;
        _processes = processes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<JunkCategoryScan>> ScanUserCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var scans = new List<JunkCategoryScan>();
        foreach (var category in JunkCatalog.ForScope(JunkScope.User))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scans.Add(await ScanCategoryAsync(category.Id, cancellationToken));
        }

        return scans;
    }

    public Task<JunkCategoryScan> ScanCategoryAsync(JunkCategoryId categoryId, CancellationToken cancellationToken = default)
    {
        var category = JunkCatalog.Get(categoryId);
        if (category.Scope != JunkScope.User)
        {
            throw new ArgumentOutOfRangeException(
                nameof(categoryId),
                categoryId,
                "System-scope categories are scanned by the elevated helper, not the shell.");
        }

        // Every branch is CPU-light but I/O-heavy; Task.Run keeps a long walk off the UI thread
        // while leaving the scanners themselves plain synchronous code.
        return Task.Run(() => categoryId switch
        {
            JunkCategoryId.UserTemp => ScanUserTemp(cancellationToken),
            JunkCategoryId.BrowserCache => ScanBrowserCaches(cancellationToken),
            JunkCategoryId.ThumbnailCache => ScanThumbnailCache(cancellationToken),
            JunkCategoryId.ShaderCache => ScanShaderCache(cancellationToken),
            JunkCategoryId.UserCrashDumps => ScanUserCrashDumps(cancellationToken),
            JunkCategoryId.RecycleBin => ScanRecycleBin(),
            _ => JunkCategoryScan.Empty(categoryId),
        }, cancellationToken);
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private JunkCategoryScan ScanUserTemp(CancellationToken cancellationToken)
    {
        var scan = FileTreeScanner.Scan(_paths.UserTemp, JunkCatalog.RecentTempFileGrace, UtcNow, cancellationToken);

        return new JunkCategoryScan(
            JunkCategoryId.UserTemp,
            scan.Items,
            scan.HadUnreadableEntries ? JunkScanNote.PartiallyUnreadable : JunkScanNote.None,
            scan.SkippedCount);
    }

    /// <summary>
    /// Caches only — never history, cookies or saved form data (ADR-0008). A browser that is
    /// running holds its cache open, so rather than fighting the lock or half-deleting a profile,
    /// that browser is left out and the UI says why.
    /// </summary>
    private JunkCategoryScan ScanBrowserCaches(CancellationToken cancellationToken)
    {
        var items = new List<JunkItem>();
        var details = new List<string>();
        int skipped = 0;
        bool anyRunning = false;
        bool unreadable = false;

        foreach ((string name, string relativePath, string processName) in BrowserCacheLocator.ChromiumBrowsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string userData = Path.Combine(_paths.LocalAppData, relativePath);
            var locations = BrowserCacheLocator.FindChromiumCaches(name, userData);
            if (locations.Count == 0)
            {
                continue;
            }

            if (_processes.IsRunning(processName))
            {
                anyRunning = true;
                details.Add(name);
                continue;
            }

            AddLocations(locations, items, details, ref skipped, ref unreadable, cancellationToken);
        }

        string firefoxProfiles = Path.Combine(_paths.LocalAppData, "Mozilla", "Firefox", "Profiles");
        var firefoxLocations = BrowserCacheLocator.FindFirefoxCaches(firefoxProfiles);
        if (firefoxLocations.Count > 0)
        {
            if (_processes.IsRunning("firefox"))
            {
                anyRunning = true;
                details.Add("Mozilla Firefox");
            }
            else
            {
                AddLocations(firefoxLocations, items, details, ref skipped, ref unreadable, cancellationToken);
            }
        }

        var note = anyRunning
            ? JunkScanNote.BrowserRunning
            : unreadable ? JunkScanNote.PartiallyUnreadable : JunkScanNote.None;

        return new JunkCategoryScan(JunkCategoryId.BrowserCache, items, note, skipped) { Details = details };
    }

    private static void AddLocations(
        IReadOnlyList<BrowserCacheLocation> locations,
        List<JunkItem> items,
        List<string> details,
        ref int skipped,
        ref bool unreadable,
        CancellationToken cancellationToken)
    {
        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A cache has no equivalent of the temp-file grace period: nothing in it is mid-install.
            var scan = FileTreeScanner.Scan(location.CachePath, TimeSpan.Zero, now: null, cancellationToken);
            items.AddRange(scan.Items);
            skipped += scan.SkippedCount;
            unreadable |= scan.HadUnreadableEntries;
        }

        foreach (var browserName in locations.Select(location => location.BrowserName).Distinct())
        {
            int profileCount = locations.Where(location => location.BrowserName == browserName)
                .Select(location => location.ProfileName)
                .Distinct()
                .Count();

            details.Add($"{browserName} ({profileCount})");
        }
    }

    /// <summary>
    /// File Explorer's thumbnail and icon databases. The folder holds other Explorer state, so only
    /// the cache databases themselves are listed — Explorer rebuilds those as you browse.
    /// </summary>
    private JunkCategoryScan ScanThumbnailCache(CancellationToken cancellationToken)
    {
        string explorerFolder = Path.Combine(_paths.LocalAppData, "Microsoft", "Windows", "Explorer");
        var scan = FileTreeScanner.Scan(explorerFolder, TimeSpan.Zero, UtcNow, cancellationToken);

        var caches = scan.Items
            .Where(item => Path.GetFileName(item.Path).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(item.Path).StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new JunkCategoryScan(JunkCategoryId.ThumbnailCache, caches, SkippedCount: scan.Items.Count - caches.Count);
    }

    private JunkCategoryScan ScanShaderCache(CancellationToken cancellationToken)
    {
        // Windows' own "DirectX Shader Cache" is exactly this folder; vendor caches (NVIDIA, AMD)
        // are deliberately not included, so what Upkeep clears matches what Windows calls it.
        string shaderCache = Path.Combine(_paths.LocalAppData, "D3DSCache");
        var scan = FileTreeScanner.Scan(shaderCache, TimeSpan.Zero, UtcNow, cancellationToken);

        return new JunkCategoryScan(JunkCategoryId.ShaderCache, scan.Items, SkippedCount: scan.SkippedCount);
    }

    private JunkCategoryScan ScanUserCrashDumps(CancellationToken cancellationToken)
    {
        string[] folders =
        [
            Path.Combine(_paths.LocalAppData, "CrashDumps"),
            Path.Combine(_paths.LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(_paths.LocalAppData, "Microsoft", "Windows", "WER", "ReportQueue"),
        ];

        var items = new List<JunkItem>();
        int skipped = 0;
        bool unreadable = false;

        foreach (string folder in folders)
        {
            var scan = FileTreeScanner.Scan(folder, TimeSpan.Zero, UtcNow, cancellationToken);
            items.AddRange(scan.Items);
            skipped += scan.SkippedCount;
            unreadable |= scan.HadUnreadableEntries;
        }

        return new JunkCategoryScan(
            JunkCategoryId.UserCrashDumps,
            items,
            unreadable ? JunkScanNote.PartiallyUnreadable : JunkScanNote.None,
            skipped);
    }

    /// <summary>
    /// The shell reports the bin as a total, not a list of paths, so this is one of the two
    /// categories that carries a reported size rather than items.
    /// </summary>
    private JunkCategoryScan ScanRecycleBin()
    {
        var info = _recycleBin.Query();

        return new JunkCategoryScan(JunkCategoryId.RecycleBin, [])
        {
            ReportedBytes = info.SizeBytes,
            ReportedItemCount = info.ItemCount,
        };
    }
}
