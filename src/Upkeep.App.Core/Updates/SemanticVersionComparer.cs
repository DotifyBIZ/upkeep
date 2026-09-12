using System.Diagnostics.CodeAnalysis;

namespace Upkeep.App.Core.Updates;

/// <summary>Compares the running assembly's version against a GitHub release tag in
/// semantic-release's default "vX.Y.Z" format. Pure, so it's testable without a network.</summary>
public static class SemanticVersionComparer
{
    public static bool IsNewer(string currentVersion, string candidateTag)
    {
        if (!TryParse(currentVersion, out var current) || !TryParse(candidateTag, out var candidate))
        {
            return false;
        }

        return Normalize(candidate) > Normalize(current);
    }

    // Ignores Revision entirely so a 4-part assembly version (MSBuild always pads to X.Y.Z.0)
    // compares correctly against a 3-part release tag, instead of tripping on System.Version's
    // "unspecified component sorts lower" rule.
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static bool TryParse(string? raw, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        return !string.IsNullOrWhiteSpace(raw) && Version.TryParse(raw.TrimStart('v', 'V'), out version);
    }
}
