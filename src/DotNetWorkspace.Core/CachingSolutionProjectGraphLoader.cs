using System.Collections.Concurrent;

namespace DotNetWorkspace.Core;

public sealed class CachingSolutionProjectGraphLoader(ISolutionProjectGraphLoader inner) : ISolutionProjectGraphLoader
{
    readonly ISolutionProjectGraphLoader _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    readonly ConcurrentDictionary<string, SolutionProjectGraph> _graphs =
        new(StringComparer.OrdinalIgnoreCase);

    public SolutionProjectGraph Load(string solutionOrProjectPath)
    {
        var key = Path.GetFullPath(solutionOrProjectPath.Trim());
        return _graphs.GetOrAdd(key, static (path, loader) => loader.Load(path), _inner);
    }

    public DotNetProjectEntry? TryResolveOwningProject(
        string filePath,
        string? solutionOrProjectPath = null,
        DotNetProjectKind? kindFilter = null)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
            return _inner.TryResolveOwningProject(filePath, null, kindFilter);

        if (!File.Exists(solutionOrProjectPath))
            return ProjectOwnershipRules.WalkUpOwningProject(filePath, kindFilter);

        var graph = Load(solutionOrProjectPath);
        return graph.TryResolveOwningProject(filePath, kindFilter)
            ?? ProjectOwnershipRules.WalkUpOwningProject(filePath, kindFilter);
    }

    public void Invalidate(string? solutionOrProjectPath = null)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
        {
            _graphs.Clear();
            _inner.Invalidate();
            return;
        }

        _graphs.TryRemove(Path.GetFullPath(solutionOrProjectPath.Trim()), out _);
        _inner.Invalidate(solutionOrProjectPath);
    }
}
