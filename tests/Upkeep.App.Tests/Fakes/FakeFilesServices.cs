using Upkeep.App.Core.Abstractions;
using Upkeep.App.Core.Files;

namespace Upkeep.App.Tests.Fakes;

/// <summary>Reports whatever duplicate groups the test says the scan found.</summary>
public sealed class FakeDuplicateFinder : IDuplicateFinder
{
    public List<DuplicateGroup> Groups { get; } = [];

    public Task<DuplicateScanResult> FindAsync(
        FileScanScope scope,
        IProgress<FileScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DuplicateScanResult(Groups, Groups.Count, 0));
}

/// <summary>Reports whatever large files the test says the scan found.</summary>
public sealed class FakeLargeFileFinder : ILargeFileFinder
{
    public List<LargeFileResult> Results { get; } = [];

    public Task<IReadOnlyList<LargeFileResult>> FindAsync(
        FileScanScope scope,
        LargeFileCriteria criteria,
        IProgress<FileScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LargeFileResult>>(Results);
}

/// <summary>Reports an empty tree; the usage tab is not what these tests are about.</summary>
public sealed class FakeDiskUsageScanner : IDiskUsageScanner
{
    public Task<DiskUsageResult> ScanAsync(
        string root,
        IProgress<FileScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DiskUsageResult(new UsageNode(root, root, 0, true), 0, 0));
}

/// <summary>Accepts any removal and reports it as done.</summary>
public sealed class FakeFileActionExecutor : IFileActionExecutor
{
    public List<FileActionRequest> Requests { get; } = [];

    public Task<FileActionOutcome> RemoveAsync(FileActionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(new FileActionOutcome("session", 0, request.Paths.Count, 0, WasQuarantined: true));
    }
}

/// <summary>Picks whatever folder the test set, or nothing.</summary>
public sealed class FakeFolderPickerService : IFolderPickerService
{
    public string? Folder { get; set; }

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult(Folder);
}
