using System.Text;
using System.Text.Json;

namespace AgentNotes.Core;

/// <summary>CDP-ADR-0210: federated corpus recall helpers (scope-first rank, substring search).</summary>
internal static class KnowledgeCorpusRecall
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed record RecallHints(string? ResolvedScope, string? PrimaryProjectId, bool ScopeOnly);

    internal static RecallHints BuildHints(string? activeScope, string? primaryProjectId, bool scopeOnly)
    {
        var scope = string.IsNullOrWhiteSpace(activeScope)
            ? null
            : NotesStorage.NormalizeCorpusScope(activeScope);
        var primary = string.IsNullOrWhiteSpace(primaryProjectId) ? null : primaryProjectId.Trim();
        return new RecallHints(scope, primary, scopeOnly);
    }

    internal static int ScopeBoost(string relativePath, RecallHints hints)
    {
        if (hints.ResolvedScope is not { Length: > 0 })
            return 0;

        var path = relativePath.Replace('\\', '/');
        var boost = 0;
        var scopePrefix = $"work/projects/{hints.ResolvedScope}/";
        if (path.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase))
            boost += 50;

        if (hints.PrimaryProjectId is { Length: > 0 })
        {
            var primaryPrefix = $"{scopePrefix}{hints.PrimaryProjectId}/";
            if (path.StartsWith(primaryPrefix, StringComparison.OrdinalIgnoreCase))
                boost += 100;
        }

        return boost;
    }

    internal static bool PassesScopeOnly(string relativePath, RecallHints hints)
    {
        if (!hints.ScopeOnly || hints.ResolvedScope is not { Length: > 0 })
            return true;

        var path = relativePath.Replace('\\', '/');
        var scopePrefix = $"work/projects/{hints.ResolvedScope}/";
        if (!path.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (hints.PrimaryProjectId is not { Length: > 0 })
            return true;

        var primaryPrefix = $"{scopePrefix}{hints.PrimaryProjectId}/";
        return path.StartsWith(primaryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Search(
        string knowledgeRoot,
        string searchDir,
        string query,
        int limit,
        RecallHints hints)
    {
        var q = query.Trim();
        if (q.Length == 0)
        {
            return JsonSerializer.Serialize(new
            {
                mode = "search",
                path = searchDir,
                query,
                total = 0,
                hits = Array.Empty<object>()
            }, JsonOptions);
        }

        var hits = new List<(int boost, bool ssot, int line, string path, string preview)>();
        var scanned = 0;

        foreach (var full in EnumerateMd(searchDir))
        {
            scanned++;
            var rel = ToRelative(knowledgeRoot, full);
            if (!PassesScopeOnly(rel, hints))
                continue;

            string text;
            try { text = File.ReadAllText(full, Encoding.UTF8); }
            catch { continue; }

            var pathHit = rel.Contains(q, StringComparison.OrdinalIgnoreCase);
            var lineNo = 0;
            var preview = "";
            var matched = pathHit;

            if (!matched)
            {
                var lines = text.Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains(q, StringComparison.OrdinalIgnoreCase))
                        continue;
                    matched = true;
                    lineNo = i + 1;
                    preview = lines[i].Trim();
                    if (preview.Length > 200)
                        preview = preview[..197] + "...";
                    break;
                }
            }
            else
            {
                preview = ExtractPreview(text);
            }

            if (!matched)
                continue;

            var tags = KnowledgeTags.ParseTagsLine(text);
            var ssot = KnowledgeTags.RoleTagsOf(tags).Contains(KnowledgeTags.RoleSsot, StringComparer.OrdinalIgnoreCase);
            var boost = ScopeBoost(rel, hints);
            hits.Add((boost, ssot, lineNo, rel, preview));
        }

        var ordered = hits
            .OrderByDescending(h => h.boost)
            .ThenByDescending(h => h.ssot)
            .ThenBy(h => h.path, StringComparer.Ordinal)
            .Take(limit)
            .Select(h => new
            {
                path = h.path,
                line = h.line > 0 ? h.line : (int?)null,
                preview = h.preview,
                scope_boost = h.boost
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            mode = "search",
            path = searchDir,
            query = q,
            active_scope = hints.ResolvedScope,
            primary_project_id = hints.PrimaryProjectId,
            scope_only = hints.ScopeOnly,
            files_scanned = scanned,
            total = ordered.Length,
            hits = ordered
        }, JsonOptions);
    }

    private static string ToRelative(string knowledgeRoot, string fullPath)
    {
        var baseLen = knowledgeRoot.Length;
        return fullPath.Length > baseLen
            ? fullPath.Substring(baseLen).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/')
            : Path.GetFileName(fullPath);
    }

    private static IEnumerable<string> EnumerateMd(string searchDir)
    {
        foreach (var full in Directory.GetFiles(searchDir, "*.md", SearchOption.AllDirectories))
        {
            if (full.Contains(".revisions", StringComparison.Ordinal))
                continue;
            var norm = full.Replace('\\', '/');
            if (norm.Contains("/scratch/", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return full;
        }
    }

    private static string ExtractPreview(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            return line.Length <= 200 ? line : line[..197] + "...";
        }

        return "";
    }
}
