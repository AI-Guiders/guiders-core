using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.ServiceLayer;

/// <summary>
/// Process-scoped MSBuild workspace cache (HCI-like): multi-root session — up to
/// <see cref="MaxRoots"/> open solutions at once. All access serialized via
/// <see cref="MsBuildWorkspaceGate"/>. CDP warms on cdp_open; tools reuse instead of
/// Create+Open+Dispose per call. Opening an unknown key adds a root (LRU eviction at cap);
/// <see cref="Invalidate()"/> drops all roots.
/// </summary>
public static class MsBuildWorkspaceHost
{
    /// <summary>Root cap — bound memory; LRU entry is evicted when exceeded.</summary>
    public const int MaxRoots = 4;

    internal sealed class RootEntry
    {
        public required string Key;
        public required string RootDir;
        public required MSBuildWorkspace Workspace;
        public DateTimeOffset LastUsedUtc;
    }

    static readonly object StateLock = new();
    static readonly List<RootEntry> Roots = new();

    static string NormalizeKey(string path) =>
        Path.GetFullPath(path.Trim());

    static string NormalizeDir(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? string.Empty);

    /// <summary>Caller must hold <see cref="MsBuildWorkspaceGate"/>. Exact key match, else longest root-dir prefix.</summary>
    static RootEntry? RouteUnlocked(string normalizedPath)
    {
        RootEntry? best = null;
        var bestLen = -1;
        foreach (var root in Roots)
        {
            if (string.Equals(root.Key, normalizedPath, StringComparison.OrdinalIgnoreCase))
                return Touch(root);

            var dir = root.RootDir;
            if (dir.Length == 0)
                continue;
            var under = normalizedPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                        && (normalizedPath.Length == dir.Length
                            || dir.Length == 3 // drive root, e.g. C:\
                            || normalizedPath[dir.Length] is '\\' or '/');
            if (under && dir.Length > bestLen)
            {
                best = root;
                bestLen = dir.Length;
            }
        }

        return best is null ? null : Touch(best);
    }

    static RootEntry Touch(RootEntry entry)
    {
        entry.LastUsedUtc = DateTimeOffset.UtcNow;
        return entry;
    }

    /// <summary>Background-friendly warm after project open. No-op if path missing.</summary>
    public static async Task WarmAsync(string solutionOrProjectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath) || !File.Exists(solutionOrProjectPath))
            return;

        await MsBuildWorkspaceGate.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureOpenUnlockedAsync(NormalizeKey(solutionOrProjectPath), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MsBuildWorkspaceGate.Gate.Release();
        }
    }

    /// <summary>
    /// Run work against the routed root (exact key first, then path prefix, else open as new root).
    /// Holds the MSBuild gate for the whole call.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        string solutionOrProjectPath,
        Func<MSBuildWorkspace, Solution, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        await MsBuildWorkspaceGate.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = await EnsureOrRouteUnlockedAsync(NormalizeKey(solutionOrProjectPath), cancellationToken)
                .ConfigureAwait(false);
            return await body(entry.Workspace, entry.Workspace.CurrentSolution, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MsBuildWorkspaceGate.Gate.Release();
        }
    }

    /// <summary>Drop all roots (project close / path change / failed open).</summary>
    public static void Invalidate()
    {
        lock (StateLock)
            DisposeAllUnlocked();
    }

    /// <summary>Drop one root by exact solution/project key (multi-root session remove).</summary>
    public static void Invalidate(string solutionOrProjectPath)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
            return;

        var key = NormalizeKey(solutionOrProjectPath);
        lock (StateLock)
        {
            var entry = Roots.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return;
            Roots.Remove(entry);
            entry.Workspace.Dispose();
        }
    }

    /// <summary>
    /// Push buffer text into whichever open root holds the document (routed root first).
    /// Returns false if no matching doc in any root.
    /// </summary>
    public static bool TryApplyDocumentText(string filePath, string text)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var full = NormalizeKey(filePath);
        MsBuildWorkspaceGate.Gate.Wait();
        try
        {
            lock (StateLock)
            {
                if (Roots.Count == 0)
                    return false;

                foreach (var entry in ScanOrderUnlocked(full))
                {
                    var doc = entry.Workspace.CurrentSolution.Projects
                        .SelectMany(p => p.Documents)
                        .FirstOrDefault(d =>
                            d.FilePath is { } fp
                            && string.Equals(NormalizeKey(fp), full, StringComparison.OrdinalIgnoreCase));
                    if (doc is null)
                        continue;

                    var updated = entry.Workspace.CurrentSolution.WithDocumentText(doc.Id, SourceText.From(text));
                    return entry.Workspace.TryApplyChanges(updated);
                }

                return false;
            }
        }
        finally
        {
            MsBuildWorkspaceGate.Gate.Release();
        }
    }

    /// <summary>Caller must hold <see cref="MsBuildWorkspaceGate"/>. Routed entry first, then rest MRU-first.</summary>
    static List<RootEntry> ScanOrderUnlocked(string normalizedPath)
    {
        var routed = RouteUnlocked(normalizedPath);
        var order = Roots
            .Where(r => !ReferenceEquals(r, routed))
            .OrderByDescending(static r => r.LastUsedUtc)
            .ToList();
        if (routed is not null)
            order.Insert(0, routed);
        return order;
    }

    /// <summary>Caller must hold <see cref="MsBuildWorkspaceGate"/>. Route; unknown keys open as new root (LRU-capped).</summary>
    internal static async Task<RootEntry> EnsureOrRouteUnlockedAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var routed = RouteUnlocked(key);
        if (routed is not null)
            return routed;

        return await EnsureOpenUnlockedAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Caller must hold <see cref="MsBuildWorkspaceGate"/>. Opens and adds a root; evicts LRU at cap.</summary>
    static async Task<RootEntry> EnsureOpenUnlockedAsync(string key, CancellationToken cancellationToken)
    {
        lock (StateLock)
        {
            var existing = Roots.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return Touch(existing);
        }

        // Open outside StateLock (async); still under gate so no parallel open.
        MsBuildLocatorOnce.EnsureRegistered();
        var workspace = MSBuildWorkspace.Create(RoslynMcpWorkspaceProperties.MsBuild);
        Solution? solution;
        try
        {
            solution = await WorkspaceOpen.OpenSolutionOrProjectAsync(workspace, key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }

        if (solution is null)
        {
            workspace.Dispose();
            throw new InvalidOperationException($"failed to open solution/project: {key}");
        }

        lock (StateLock)
        {
            var duplicate = Roots.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                workspace.Dispose();
                return Touch(duplicate);
            }

            while (Roots.Count >= MaxRoots)
                EvictLruUnlocked();

            var entry = new RootEntry
            {
                Key = key,
                RootDir = NormalizeDir(key),
                Workspace = workspace,
                LastUsedUtc = DateTimeOffset.UtcNow,
            };
            Roots.Add(entry);
            return entry;
        }
    }

    /// <summary>Caller must hold <see cref="MsBuildWorkspaceGate"/> and be at cap.</summary>
    static void EvictLruUnlocked()
    {
        if (Roots.Count == 0)
            return;
        var lru = Roots.MinBy(static r => r.LastUsedUtc);
        Roots.Remove(lru!);
        lru!.Workspace.Dispose();
    }

    static void DisposeAllUnlocked()
    {
        foreach (var root in Roots)
            root.Workspace.Dispose();
        Roots.Clear();
    }
}
