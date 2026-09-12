using Upkeep.App.Core.Cleanup;

namespace Upkeep.App.Core.Tests.Cleanup;

public class BrowserCacheLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"upkeep-browser-{Guid.NewGuid():N}");

    public BrowserCacheLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateChromiumProfile(string profileName, params string[] cacheFolders)
    {
        string profile = Path.Combine(_root, profileName);
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "Preferences"), "{}");

        foreach (string cacheFolder in cacheFolders)
        {
            Directory.CreateDirectory(Path.Combine(profile, cacheFolder));
        }

        return profile;
    }

    [Fact]
    public void FindChromiumCaches_FindsEveryProfilesCacheDirectories()
    {
        CreateChromiumProfile("Default", Path.Combine("Cache", "Cache_Data"), "Code Cache");
        CreateChromiumProfile("Profile 1", "GPUCache");

        var found = BrowserCacheLocator.FindChromiumCaches("Google Chrome", _root);

        Assert.Equal(3, found.Count);
        Assert.Contains(found, location => location.ProfileName == "Default" && location.CachePath.EndsWith("Cache_Data", StringComparison.Ordinal));
        Assert.Contains(found, location => location.ProfileName == "Profile 1");
        Assert.All(found, location => Assert.Equal("Google Chrome", location.BrowserName));
    }

    [Fact]
    public void FindChromiumCaches_IgnoresFoldersThatArentProfiles()
    {
        // "ShaderCache" and friends sit beside the profiles under User Data. A profile is a folder
        // with a Preferences file — the same test the browser itself applies.
        Directory.CreateDirectory(Path.Combine(_root, "ShaderCache", "GPUCache"));
        CreateChromiumProfile("Default", "GPUCache");

        var found = BrowserCacheLocator.FindChromiumCaches("Microsoft Edge", _root);

        Assert.Single(found);
        Assert.Equal("Default", found[0].ProfileName);
    }

    [Fact]
    public void FindChromiumCaches_ProfileWithNoCacheYet_ReturnsNothingForIt()
    {
        CreateChromiumProfile("Default");

        Assert.Empty(BrowserCacheLocator.FindChromiumCaches("Microsoft Edge", _root));
    }

    [Fact]
    public void FindChromiumCaches_MissingUserDataFolder_ReturnsEmpty() =>
        Assert.Empty(BrowserCacheLocator.FindChromiumCaches("Google Chrome", Path.Combine(_root, "not-installed")));

    [Fact]
    public void FindFirefoxCaches_FindsCache2PerProfile()
    {
        Directory.CreateDirectory(Path.Combine(_root, "abc123.default-release", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "xyz789.dev-edition", "cache2"));
        Directory.CreateDirectory(Path.Combine(_root, "no-cache-yet"));

        var found = BrowserCacheLocator.FindFirefoxCaches(_root);

        Assert.Equal(2, found.Count);
        Assert.All(found, location => Assert.Equal("Mozilla Firefox", location.BrowserName));
        Assert.All(found, location => Assert.EndsWith("cache2", location.CachePath, StringComparison.Ordinal));
    }

    [Fact]
    public void FindFirefoxCaches_MissingProfilesFolder_ReturnsEmpty() =>
        Assert.Empty(BrowserCacheLocator.FindFirefoxCaches(Path.Combine(_root, "no-firefox")));

    [Fact]
    public void ChromiumBrowsers_CoverEdgeAndChromeWithTheirProcessNames()
    {
        Assert.Contains(BrowserCacheLocator.ChromiumBrowsers, browser => browser.ProcessName == "msedge");
        Assert.Contains(BrowserCacheLocator.ChromiumBrowsers, browser => browser.ProcessName == "chrome");
    }
}
