using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Logging;
using Upkeep.App.Core.Settings;
using Upkeep.App.Core.Updates;

namespace Upkeep.App.Services;

/// <summary>
/// Upkeep's only outbound call of its own: one unauthenticated GET to GitHub's public release
/// list, compared against this build's version. See docs/adr/0004-update-check-network-call.md.
/// Nothing identifying is sent, nothing is downloaded, and a failure is indistinguishable from
/// "you're up to date".
/// </summary>
public sealed class GitHubUpdateCheckService : IUpdateCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/DotifyBIZ/upkeep/releases/latest";

    /// <summary>This runs on the Home page's load path, so it gets its own short timeout rather
    /// than HttpClient's 100-second default.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A release payload is a few KB; anything far larger is not something to parse.</summary>
    private const int MaxResponseBytes = 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _settingsService;
    private readonly IAppLogger _logger;

    public GitHubUpdateCheckService(IHttpClientFactory httpClientFactory, IAppSettingsService settingsService, IAppLogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        if (!settings.CheckForUpdatesEnabled)
        {
            return UpdateCheckResult.NoUpdate;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.ParseAdd("Upkeep-UpdateCheck");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                await _logger.LogWarningAsync($"Update check returned {(int)response.StatusCode}.", cancellationToken);
                return UpdateCheckResult.Failed;
            }

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                await _logger.LogWarningAsync("Update check response was larger than expected and was ignored.", cancellationToken);
                return UpdateCheckResult.Failed;
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(timeout.Token);
            if (release?.TagName is null)
            {
                await _logger.LogWarningAsync("Update check got an answer with no release tag in it.", cancellationToken);
                return UpdateCheckResult.Failed;
            }

            string currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            return SemanticVersionComparer.IsNewer(currentVersion, release.TagName)
                ? new UpdateCheckResult(true, release.TagName.TrimStart('v', 'V'), release.HtmlUrl)
                : UpdateCheckResult.NoUpdate;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Offline, GitHub down, rate-limited, malformed: all mean the same thing to the user.
            await _logger.LogWarningAsync($"Update check did not complete: {ex.Message}", cancellationToken);
            return UpdateCheckResult.Failed;
        }
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
