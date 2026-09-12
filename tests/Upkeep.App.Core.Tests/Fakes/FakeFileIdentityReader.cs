using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Tests.Fakes;

/// <summary>
/// Reports whatever on-disk identity a test wants, so hard-link handling can be exercised without
/// creating hard links on the machine running the tests. Unregistered paths each get their own
/// identity, which is the normal case: separate files.
/// </summary>
public sealed class FakeFileIdentityReader : IFileIdentityReader
{
    private readonly Dictionary<string, FileId> _identities = new(StringComparer.OrdinalIgnoreCase);
    private ulong _nextIndex = 1000;

    /// <summary>Makes every given path report the same identity — i.e. hard links to one file.</summary>
    public void LinkTogether(params string[] paths)
    {
        var shared = new FileId(1, _nextIndex++);
        foreach (string path in paths)
        {
            _identities[path] = shared;
        }
    }

    /// <summary>Makes a path report no identity at all, as Windows does when it won't answer.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FileId? TryRead(string path)
    {
        if (Unreadable.Contains(path))
        {
            return null;
        }

        if (!_identities.TryGetValue(path, out var id))
        {
            id = new FileId(1, _nextIndex++);
            _identities[path] = id;
        }

        return id;
    }
}
