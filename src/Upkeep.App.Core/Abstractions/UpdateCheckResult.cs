namespace Upkeep.App.Core.Abstractions;

public sealed record UpdateCheckResult(bool IsUpdateAvailable, string? LatestVersion, string? ReleaseUrl)
{
    public static readonly UpdateCheckResult NoUpdate = new(false, null, null);
}
