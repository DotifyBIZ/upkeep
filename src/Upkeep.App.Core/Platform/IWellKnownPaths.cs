namespace Upkeep.App.Core.Platform;

/// <summary>
/// Where Windows keeps the things Upkeep cleans. Behind an interface so scanners can be pointed
/// at a temporary tree in tests instead of the machine running them — a cleaning tool whose tests
/// exercise the real %TEMP% is a bad idea exactly once.
/// </summary>
public interface IWellKnownPaths
{
    /// <summary>The signed-in user's temp folder (%TEMP%).</summary>
    string UserTemp { get; }

    /// <summary>%LocalAppData%.</summary>
    string LocalAppData { get; }

    /// <summary>%AppData% (roaming).</summary>
    string RoamingAppData { get; }

    /// <summary>%ProgramData%.</summary>
    string ProgramData { get; }

    /// <summary>The Windows directory (usually C:\Windows).</summary>
    string WindowsDirectory { get; }

    /// <summary>The root of the drive Windows is installed on.</summary>
    string SystemDriveRoot { get; }

    /// <summary>The folder holding user profiles (usually C:\Users).</summary>
    string UserProfilesRoot { get; }

    /// <summary>The current user's profile folder.</summary>
    string UserProfile { get; }
}
