using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Upkeep.App.Core.Sessions;

/// <summary>Reads and writes session manifests — the undo log behind the History page.</summary>
public interface ISessionJournal
{
    /// <summary>Starts a session and writes it to disk immediately, before any work happens.</summary>
    Task<SessionManifest> StartAsync(SessionKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an entry *before* the action it describes is carried out, and returns the updated
    /// manifest. Call again with <paramref name="entry"/> marked completed once it has run.
    /// </summary>
    Task<SessionManifest> AppendAsync(SessionManifest manifest, SessionEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Replaces an entry (typically to mark it completed or failed).</summary>
    Task<SessionManifest> UpdateEntryAsync(SessionManifest manifest, int entryIndex, SessionEntry entry, CancellationToken cancellationToken = default);

    Task<SessionManifest> SaveAsync(SessionManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>Every session, newest first. Unreadable files are skipped rather than failing the page.</summary>
    Task<IReadOnlyList<SessionManifest>> ListAsync(CancellationToken cancellationToken = default);

    Task<SessionManifest?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// One JSON file per session under %LocalAppData%\Upkeep\Sessions. Small enough to rewrite on
/// every append, which is what keeps the file honest if the process dies mid-run.
/// </summary>
public sealed class SessionJournal : ISessionJournal, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    [ExcludeFromCodeCoverage(Justification = "Resolves a well-known Windows folder; the injectable constructor below is covered.")]
    public SessionJournal()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Upkeep", "Sessions"))
    {
    }

    public SessionJournal(string directory, TimeProvider? timeProvider = null)
    {
        _directory = directory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Directory => _directory;

    public void Dispose() => _writeLock.Dispose();

    public async Task<SessionManifest> StartAsync(SessionKind kind, CancellationToken cancellationToken = default)
    {
        var startedAt = _timeProvider.GetUtcNow();
        var manifest = new SessionManifest
        {
            Id = SessionManifest.NewId(startedAt),
            Kind = kind,
            StartedAt = startedAt,
        };

        return await SaveAsync(manifest, cancellationToken);
    }

    public Task<SessionManifest> AppendAsync(SessionManifest manifest, SessionEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var updated = manifest with { Entries = [.. manifest.Entries, entry] };
        return SaveAsync(updated, cancellationToken);
    }

    public Task<SessionManifest> UpdateEntryAsync(SessionManifest manifest, int entryIndex, SessionEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryIndex, manifest.Entries.Count);

        var entries = manifest.Entries.ToArray();
        entries[entryIndex] = entry;

        return SaveAsync(manifest with { Entries = entries }, cancellationToken);
    }

    public async Task<SessionManifest> SaveAsync(SessionManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            string path = PathFor(manifest.Id);

            // Write beside the target and swap: a half-written manifest is worse than none, since
            // it is what a revert would read.
            string temporaryPath = path + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }

        return manifest;
    }

    public async Task<IReadOnlyList<SessionManifest>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        var sessions = new List<SessionManifest>();
        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifest = await ReadAsync(file, cancellationToken);
            if (manifest is not null)
            {
                sessions.Add(manifest);
            }
        }

        return [.. sessions.OrderByDescending(session => session.StartedAt)];
    }

    public Task<SessionManifest?> LoadAsync(string sessionId, CancellationToken cancellationToken = default) =>
        ReadAsync(PathFor(sessionId), cancellationToken);

    private string PathFor(string sessionId)
    {
        // Session ids are generated by this class, but they also arrive from the UI and from disk;
        // a traversal through this path would let a crafted id write outside the journal.
        string fileName = Path.GetFileName(sessionId);
        if (string.IsNullOrWhiteSpace(fileName) || fileName != sessionId)
        {
            throw new ArgumentException("Session ids may not contain path separators.", nameof(sessionId));
        }

        return Path.Combine(_directory, $"{fileName}.json");
    }

    private static async Task<SessionManifest?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SessionManifest>(stream, SerializerOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // One unreadable manifest must not take down the whole History page.
            return null;
        }
    }
}
