namespace Upkeep.App.Core.Abstractions;

/// <summary>
/// Checks GitHub Releases for a newer published version than this build — Upkeep's only outbound
/// call of its own. Gated behind AppSettings.CheckForUpdatesEnabled; see
/// docs/adr/0004-update-check-network-call.md. Implemented in Upkeep.App via IHttpClientFactory.
/// </summary>
public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);
}
