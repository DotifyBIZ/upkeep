using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>Reports whatever the test says the Recycle Bin holds, and records an empty request.</summary>
public sealed class FakeRecycleBin : IRecycleBin
{
    public RecycleBinInfo Info { get; set; } = RecycleBinInfo.Empty;

    public bool WasEmptied { get; private set; }

    public RecycleBinInfo Query() => Info;

    public bool Empty()
    {
        WasEmptied = true;
        return true;
    }
}

/// <summary>Pretends the named processes are running.</summary>
public sealed class FakeRunningProcesses : IRunningProcesses
{
    private readonly HashSet<string> _running;

    public FakeRunningProcesses(params string[] running) =>
        _running = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);

    public bool IsRunning(string processName) => _running.Contains(processName);
}

/// <summary>A clock that doesn't move, so the temp-file grace period is testable.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
