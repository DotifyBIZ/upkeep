namespace Upkeep.App.Core.Cleanup;

/// <summary>A browser's cache directories, per profile, plus the process that locks them.</summary>
public sealed record BrowserCacheLocation(string BrowserName, string ProfileName, string CachePath);

/// <summary>
/// Finds browser cache directories — caches only. History, cookies and saved form data are
/// deliberately out of scope for v1 (ADR-0008); the difference between "your pages load a little
/// slower" and "you are signed out of everything" is the whole reason that line exists.
/// </summary>
public static class BrowserCacheLocator
{
    /// <summary>Chromium keeps each profile in its own folder; these are the cache directories
    /// inside one, in the order Explorer would show them.</summary>
    private static readonly string[] ChromiumCacheFolders =
    [
        Path.Combine("Cache", "Cache_Data"),
        "Code Cache",
        "GPUCache",
        "ShaderCache",
    ];

    /// <summary>Chromium browsers Upkeep looks for, as (display name, path under %LocalAppData%).</summary>
    public static readonly IReadOnlyList<(string Name, string RelativePath, string ProcessName)> ChromiumBrowsers =
    [
        ("Microsoft Edge", Path.Combine("Microsoft", "Edge", "User Data"), "msedge"),
        ("Google Chrome", Path.Combine("Google", "Chrome", "User Data"), "chrome"),
    ];

    /// <summary>
    /// Cache directories for every profile under a Chromium "User Data" folder. A profile is a
    /// folder with a Preferences file in it — which is how the browser itself decides, and avoids
    /// mistaking support folders like "ShaderCache" or "GrShaderCache" for profiles.
    /// </summary>
    public static IReadOnlyList<BrowserCacheLocation> FindChromiumCaches(string browserName, string userDataPath)
    {
        var locations = new List<BrowserCacheLocation>();
        if (!Directory.Exists(userDataPath))
        {
            return locations;
        }

        foreach (string profileDirectory in SafeEnumerateDirectories(userDataPath))
        {
            if (!File.Exists(Path.Combine(profileDirectory, "Preferences")))
            {
                continue;
            }

            string profileName = Path.GetFileName(profileDirectory);
            foreach (string cacheFolder in ChromiumCacheFolders)
            {
                string cachePath = Path.Combine(profileDirectory, cacheFolder);
                if (Directory.Exists(cachePath))
                {
                    locations.Add(new BrowserCacheLocation(browserName, profileName, cachePath));
                }
            }
        }

        return locations;
    }

    /// <summary>
    /// Firefox keeps caches outside the profile directory, under %LocalAppData%\Mozilla\Firefox\
    /// Profiles\&lt;profile&gt;\cache2 — the profile folder under %AppData% holds the data that is
    /// deliberately not touched here.
    /// </summary>
    public static IReadOnlyList<BrowserCacheLocation> FindFirefoxCaches(string firefoxProfilesPath)
    {
        var locations = new List<BrowserCacheLocation>();
        if (!Directory.Exists(firefoxProfilesPath))
        {
            return locations;
        }

        foreach (string profileDirectory in SafeEnumerateDirectories(firefoxProfilesPath))
        {
            string cachePath = Path.Combine(profileDirectory, "cache2");
            if (Directory.Exists(cachePath))
            {
                locations.Add(new BrowserCacheLocation("Mozilla Firefox", Path.GetFileName(profileDirectory), cachePath));
            }
        }

        return locations;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
