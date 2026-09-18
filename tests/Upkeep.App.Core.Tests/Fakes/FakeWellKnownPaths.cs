using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>
/// Points every well-known location at a throwaway tree. A cleaning tool whose tests run against
/// the real %TEMP% is a bad idea exactly once.
/// </summary>
public sealed class FakeWellKnownPaths : IWellKnownPaths, IDisposable
{
    private readonly string _root;

    public FakeWellKnownPaths()
    {
        _root = Path.Combine(Path.GetTempPath(), $"upkeep-paths-{Guid.NewGuid():N}");

        UserTemp = CreateUnder("Temp");
        LocalAppData = CreateUnder("LocalAppData");
        RoamingAppData = CreateUnder("AppData");
        ProgramData = CreateUnder("ProgramData");
        WindowsDirectory = CreateUnder("Windows");
        UserProfilesRoot = CreateUnder("Users");
        UserProfile = CreateUnder(Path.Combine("Users", "tester"));
        SystemDriveRoot = _root;
    }

    public string UserTemp { get; }

    public string LocalAppData { get; }

    public string RoamingAppData { get; }

    public string ProgramData { get; }

    public string WindowsDirectory { get; }

    /// <summary>Settable so a test can use a real drive root ("C:\"), which behaves differently
    /// from an ordinary folder: it already ends in a separator.</summary>
    public string SystemDriveRoot { get; set; }

    public string UserProfilesRoot { get; }

    public string UserProfile { get; }

    /// <summary>Creates a folder under the fake tree and returns its full path.</summary>
    public string CreateUnder(string relativePath)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Writes a file of <paramref name="sizeBytes"/> zero bytes, optionally aged.</summary>
    public static string WriteFile(string fullPath, int sizeBytes, DateTime? lastWriteUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);

        if (lastWriteUtc is not null)
        {
            File.SetLastWriteTimeUtc(fullPath, lastWriteUtc.Value);
        }

        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
