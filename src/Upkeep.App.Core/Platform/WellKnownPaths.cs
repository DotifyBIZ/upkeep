using System.Diagnostics.CodeAnalysis;

namespace Upkeep.App.Core.Platform;

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Reads well-known Windows locations; every scanner that consumes it is tested against a substitute.")]
public sealed class WellKnownPaths : IWellKnownPaths
{
    public string UserTemp { get; } = Path.GetTempPath();

    public string LocalAppData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string RoamingAppData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public string ProgramData { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public string WindowsDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public string SystemDriveRoot { get; } =
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

    public string UserProfile { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Derived from the current profile rather than assumed to be C:\Users: the profile root is
    // relocatable, and on a domain-joined machine it regularly is.
    public string UserProfilesRoot { get; } =
        Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        ?? Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\", "Users");
}
