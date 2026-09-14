using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Upkeep.App.Core.Settings;

/// <summary>
/// Reads and writes app preferences as JSON at %LocalAppData%\Upkeep\settings.json. A missing or
/// corrupt file falls back to defaults rather than failing startup; a failed save just means the
/// preference is asked again next launch.
/// </summary>
public sealed class FileAppSettingsService : IAppSettingsService
{
    private readonly string _filePath;

    [ExcludeFromCodeCoverage(Justification = "Resolves a well-known Windows folder; the injectable constructor below is covered.")]
    public FileAppSettingsService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Upkeep", "settings.json"))
    {
    }

    public FileAppSettingsService(string filePath) => _filePath = filePath;

    /// <summary>
    /// Reads settings synchronously, for the one caller that has to: the language has to be
    /// chosen before the UI thread exists (see the shell's StartupLanguage). Everywhere else uses
    /// <see cref="LoadAsync"/>.
    /// </summary>
    public static AppSettings LoadForStartup(string? filePath = null)
    {
        filePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Upkeep", "settings.json");

        try
        {
            return File.Exists(filePath)
                ? Sanitize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath)) ?? new AppSettings())
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken) ?? new AppSettings();
            return Sanitize(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            if (Path.GetDirectoryName(_filePath) is string settingsFolder)
            {
                Directory.CreateDirectory(settingsFolder);
            }
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed save (disk full, permissions) just reverts the preference next launch —
            // not worth failing whatever action triggered it.
        }
    }

    // This file sits in a user-writable directory, so treat it as untrusted input: a hand-edited
    // retention of 0 or 9999 days would otherwise decide when real files get destroyed.
    private static AppSettings Sanitize(AppSettings settings)
    {
        settings.QuarantineRetentionDays = Math.Clamp(settings.QuarantineRetentionDays, 1, 90);

        if (settings.LanguageOverride is not ("" or "en-US" or "pl-PL"))
        {
            settings.LanguageOverride = string.Empty;
        }

        return settings;
    }
}
