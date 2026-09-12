using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Quarantine;
using Upkeep.App.Core.Settings;

namespace Upkeep.App.Tests.Fakes;

/// <summary>Keeps settings in memory, and remembers every save so a test can assert what stuck.</summary>
public sealed class FakeAppSettingsService : IAppSettingsService
{
    public AppSettings Settings { get; set; } = new();

    public List<AppSettings> Saves { get; } = [];

    /// <summary>Set to make the file unwritable, exercising the failure path.</summary>
    public bool ThrowOnSave { get; set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave)
        {
            throw new IOException("The settings file is not writable.");
        }

        Settings = settings;
        Saves.Add(settings);
        return Task.CompletedTask;
    }
}

/// <summary>Answers the update check however the test needs, including not answering at all.</summary>
public sealed class FakeUpdateCheckService : IUpdateCheckService
{
    public UpdateCheckResult Result { get; set; } = UpdateCheckResult.NoUpdate;

    /// <summary>Set to simulate being offline, which is a normal outcome rather than an error.</summary>
    public bool ThrowNetworkFailure { get; set; }

    public int CheckCount { get; private set; }

    public Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        CheckCount++;

        return ThrowNetworkFailure
            ? throw new HttpRequestException("No such host is known.")
            : Task.FromResult(Result);
    }
}

/// <summary>A quarantine that only reports and forgets what a test told it to hold.</summary>
public sealed class FakeQuarantineStore : IQuarantineStore
{
    public long TotalBytes { get; set; }

    public bool WasPurged { get; private set; }

    public Task<QuarantineResult> QuarantineAsync(string path, string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuarantineResult(true, Path.Combine(@"C:\Quarantine", Path.GetFileName(path)), null));

    public Task<bool> RestoreAsync(string quarantinePath, string originalPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public long GetTotalBytes() => TotalBytes;

    public long Purge(TimeSpan retention, DateTimeOffset now) => PurgeAll();

    public long PurgeAll()
    {
        long freed = TotalBytes;
        TotalBytes = 0;
        WasPurged = true;
        return freed;
    }
}
