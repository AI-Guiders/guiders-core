using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AgentNotes.Core;

/// <summary>
/// MLP tag index over knowledge/**/*.md: cache, aliases, explain, related co-occurrence.
/// </summary>
public static partial class KnowledgeTagIndex
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Dictionary<string, string> BuiltInAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nothing about us without us"] = "equal-standing",
        ["ничего о нас без нас"] = "equal-standing",
        ["equal standing"] = "equal-standing",
        ["agent equal standing"] = "equal-standing",
        ["standing of agents"] = "equal-standing",
        ["стояние агентов"] = "equal-standing",
        ["adcm"] = "adcm",
        ["agent-driven context"] = "adcm",
        ["agent driven context management"] = "adcm",
        ["compaction"] = "adcm",
        ["precompact"] = "adcm",
        ["context management"] = "adcm",
        ["fs relocate"] = "fs-relocate",
        ["fs_relocate"] = "fs-relocate",
        ["write+delete"] = "fs-relocate",
        ["agent affordances"] = "agent-affordances",
        ["налог на героизм"] = "agent-affordances",
        ["harness affordances"] = "agent-affordances",
        ["kb taxonomy"] = "kb-taxonomy",
        ["taxonomy"] = "kb-taxonomy",
        ["hashtags"] = "kb-tags",
        ["topic tags"] = "kb-tags",
        ["canon map"] = "canon-map",
        ["dialogue delta"] = "dialogue-delta",
        ["historiography"] = "historiography",
        ["see history"] = "historiography",
    };

    public static void Invalidate(string knowledgeRootAbsolute)
    {
        if (string.IsNullOrWhiteSpace(knowledgeRootAbsolute))
            return;
        var root = Path.GetFullPath(knowledgeRootAbsolute);
        foreach (var key in Cache.Keys.ToArray())
        {
            if (key.Equals(root, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    public static string Query(
        string knowledgeRoot,
        string? searchDir,
        string? mode,
        string? tagOrQuery,
        bool ssotOnly,
        bool includeRelated,
        int limit,
        bool refresh)
    {
        knowledgeRoot = Path.GetFullPath(knowledgeRoot);
        searchDir ??= knowledgeRoot;
        searchDir = Path.GetFullPath(searchDir);
        var lim = Math.Clamp(limit, 1, 500);
        var m = (mode ?? "").Trim().ToLowerInvariant();
        if (m is "" or "auto")
            m = string.IsNullOrWhiteSpace(tagOrQuery) ? "inventory" : "lookup";

        if (!Directory.Exists(searchDir))
        {
            return JsonSerializer.Serialize(new
            {
                mode = m,
                path = searchDir,
                files_scanned = 0,
                cache = "miss",
                total = 0,
                hits = Array.Empty<object>(),
                tags = Array.Empty<object>()
            }, JsonOptions);
        }

        var index = GetOrBuild(knowledgeRoot, searchDir, refresh, out var cacheState);
        var aliases = LoadAliases(knowledgeRoot);

        if (m is "aliases")
        {
            return JsonSerializer.Serialize(new
            {
                mode = "aliases",
                path = searchDir,
                cache = cacheState,
                total = aliases.Count,
                aliases = aliases
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new { phrase = kv.Key, tag = "#" + kv.Value })
                    .Take(lim)
                    .ToArray()
            }, JsonOptions);
        }

        string? resolvedTag = null;
        string? resolveVia = null;
        if (!string.IsNullOrWhiteSpace(tagOrQuery))
        {
            var direct = KnowledgeTags.NormalizeOne(tagOrQuery);
            if (direct is not null && (index.ByTag.ContainsKey(direct) || KnowledgeTags.IsRoleTag(direct)))
            {
                resolvedTag = direct;
                resolveVia = "tag";
            }
            else
            {
                var phrase = tagOrQuery.Trim().TrimStart('#').ToLowerInvariant();
                if (aliases.TryGetValue(phrase, out var mapped))
                {
                    resolvedTag = mapped;
                    resolveVia = "alias";
                }
                else
                {
                    // fuzzy: contains key or key contains phrase
                    var hit = aliases
                        .Select(kv => (kv.Key, kv.Value, score: AliasScore(phrase, kv.Key)))
                        .Where(x => x.score > 0)
                        .OrderByDescending(x => x.score)
                        .ThenBy(x => x.Key.Length)
                        .FirstOrDefault();
                    if (hit.Value is not null)
                    {
                        resolvedTag = hit.Value;
                        resolveVia = "alias_fuzzy";
                    }
                    else if (direct is not null)
                    {
                        resolvedTag = direct;
                        resolveVia = "tag_unindexed";
                    }
                }
            }
        }

        if (m is "resolve")
        {
            return JsonSerializer.Serialize(new
            {
                mode = "resolve",
                path = searchDir,
                cache = cacheState,
                query = tagOrQuery,
                resolved_tag = resolvedTag is null ? null : "#" + resolvedTag,
                resolve_via = resolveVia,
                known = resolvedTag is not null && index.ByTag.ContainsKey(resolvedTag)
            }, JsonOptions);
        }

        if (m is "inventory" || (resolvedTag is null && m is not "lookup" and not "explain"))
        {
            var inventory = index.ByTag
                .Select(kv => new
                {
                    tag = "#" + kv.Key,
                    file_count = kv.Value.Count,
                    ssot_count = kv.Value.Count(e => e.Ssot),
                    is_role = KnowledgeTags.IsRoleTag(kv.Key)
                })
                .OrderByDescending(x => x.ssot_count)
                .ThenByDescending(x => x.file_count)
                .ThenBy(x => x.tag, StringComparer.OrdinalIgnoreCase)
                .Take(lim)
                .ToArray();

            return JsonSerializer.Serialize(new
            {
                mode = "inventory",
                path = searchDir,
                cache = cacheState,
                files_scanned = index.FilesScanned,
                tagged_files = index.Entries.Count,
                total_tags = index.ByTag.Count,
                tags = inventory
            }, JsonOptions);
        }

        if (resolvedTag is null)
        {
            return JsonSerializer.Serialize(new
            {
                mode = m,
                path = searchDir,
                cache = cacheState,
                query = tagOrQuery,
                resolved_tag = (string?)null,
                resolve_via = resolveVia,
                policy = "unknown_tag",
                files_scanned = index.FilesScanned,
                total = 0,
                hits = Array.Empty<object>(),
                related = Array.Empty<object>()
            }, JsonOptions);
        }

        var entries = index.ByTag.TryGetValue(resolvedTag, out var list)
            ? list
            : [];
        if (ssotOnly)
            entries = entries.Where(e => e.Ssot).ToList();

        var ordered = entries
            .OrderByDescending(e => e.Ssot)
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .Take(lim)
            .ToList();

        object[] related = [];
        if (includeRelated && ordered.Count > 0)
        {
            related = BuildRelated(index, resolvedTag, lim: Math.Min(12, lim))
                .Select(x => new { tag = "#" + x.Tag, co_files = x.Count })
                .Cast<object>()
                .ToArray();
        }

        if (m is "explain")
        {
            var primary = ordered.FirstOrDefault();
            return JsonSerializer.Serialize(new
            {
                mode = "explain",
                path = searchDir,
                cache = cacheState,
                query = tagOrQuery,
                resolved_tag = "#" + resolvedTag,
                resolve_via = resolveVia,
                policy = primary?.Ssot == true ? "cite_ssot" : (ordered.Count > 0 ? "cite_best_effort" : "unknown_tag"),
                ssot = primary is null ? null : new
                {
                    path = primary.Path,
                    tags = primary.Tags.Select(t => "#" + t).ToArray(),
                    preview = primary.Preview
                },
                also = ordered.Skip(1).Take(5).Select(e => new
                {
                    path = e.Path,
                    ssot = e.Ssot,
                    preview = e.Preview
                }).ToArray(),
                related,
                total = ordered.Count,
                files_scanned = index.FilesScanned
            }, JsonOptions);
        }

        // lookup
        return JsonSerializer.Serialize(new
        {
            mode = "lookup",
            path = searchDir,
            cache = cacheState,
            query = tagOrQuery,
            resolved_tag = "#" + resolvedTag,
            resolve_via = resolveVia,
            files_scanned = index.FilesScanned,
            total = ordered.Count,
            hits = ordered.Select(e => new
            {
                path = e.Path,
                tags = e.Tags.Select(t => "#" + t).ToArray(),
                topics = e.Topics.Select(t => "#" + t).ToArray(),
                roles = e.Roles.Select(t => "#" + t).ToArray(),
                ssot = e.Ssot,
                preview = e.Preview
            }).ToArray(),
            related = includeRelated ? related : Array.Empty<object>()
        }, JsonOptions);
    }

    private static int AliasScore(string phrase, string key)
    {
        if (phrase.Equals(key, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (phrase.Contains(key, StringComparison.OrdinalIgnoreCase))
            return 80;
        if (key.Contains(phrase, StringComparison.OrdinalIgnoreCase) && phrase.Length >= 4)
            return 60;
        return 0;
    }

    private static List<(string Tag, int Count)> BuildRelated(IndexData index, string tag, int lim)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!index.ByTag.TryGetValue(tag, out var files))
            return [];
        foreach (var e in files)
        {
            foreach (var t in e.Topics)
            {
                if (t.Equals(tag, StringComparison.OrdinalIgnoreCase))
                    continue;
                counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            }
        }
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(lim)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static Dictionary<string, string> LoadAliases(string knowledgeRoot)
    {
        var map = new Dictionary<string, string>(BuiltInAliases, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(knowledgeRoot, "META", "kb-topic-tag-aliases-v1.json");
        if (!File.Exists(path))
            return map;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("aliases", out var al) && al.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in al.EnumerateObject())
                {
                    var tag = KnowledgeTags.NormalizeOne(p.Value.GetString());
                    if (tag is not null)
                        map[p.Name.Trim()] = tag;
                }
            }
        }
        catch
        {
            // keep built-in
        }
        return map;
    }

    private static IndexData GetOrBuild(string knowledgeRoot, string searchDir, bool refresh, out string cacheState)
    {
        var stamp = ComputeStamp(searchDir);
        var key = searchDir;
        if (!refresh && Cache.TryGetValue(key, out var cached) && cached.Stamp == stamp)
        {
            cacheState = "hit";
            return cached.Data;
        }

        cacheState = refresh ? "refresh" : "miss";
        var data = Build(knowledgeRoot, searchDir);
        Cache[key] = new CacheEntry(stamp, data);
        return data;
    }

    private static string ComputeStamp(string searchDir)
    {
        long maxTicks = 0;
        var count = 0;
        foreach (var full in EnumerateMd(searchDir))
        {
            count++;
            try
            {
                var t = File.GetLastWriteTimeUtc(full).Ticks;
                if (t > maxTicks) maxTicks = t;
            }
            catch { /* skip */ }
        }
        return $"{count}:{maxTicks}";
    }

    private static IndexData Build(string knowledgeRoot, string searchDir)
    {
        var baseLen = knowledgeRoot.Length;
        var entries = new List<FileEntry>();
        var byTag = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var full in EnumerateMd(searchDir))
        {
            scanned++;
            string text;
            try { text = File.ReadAllText(full, Encoding.UTF8); }
            catch { continue; }

            var tags = KnowledgeTags.ParseTagsLine(text);
            if (tags.Count == 0)
                continue;

            var rel = full.Length > baseLen
                ? full.Substring(baseLen).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/')
                : Path.GetFileName(full);
            var topics = KnowledgeTags.TopicTags(tags).ToArray();
            var roles = KnowledgeTags.RoleTagsOf(tags).ToArray();
            var ssot = roles.Contains(KnowledgeTags.RoleSsot, StringComparer.OrdinalIgnoreCase);
            var preview = ExtractPreview(text);
            var entry = new FileEntry(rel, tags.ToArray(), topics, roles, ssot, preview);

            entries.Add(entry);
            foreach (var t in tags)
            {
                if (!byTag.TryGetValue(t, out var list))
                {
                    list = [];
                    byTag[t] = list;
                }
                list.Add(entry);
            }
        }

        return new IndexData(scanned, entries, byTag);
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
        var buf = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (buf.Count > 0) break;
                continue;
            }
            if (line.StartsWith('#')) continue;
            if (line.StartsWith("**Tags:**", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("**Domain:**", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("**project-id:**", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("**Статус:**", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("**Status:**", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("**Связь:**", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("---")) continue;
            buf.Add(line);
            if (buf.Count >= 3) break;
        }
        var s = string.Join(" ", buf);
        return s.Length <= 320 ? s : s[..317] + "...";
    }

    private sealed record CacheEntry(string Stamp, IndexData Data);
    private sealed record IndexData(
        int FilesScanned,
        List<FileEntry> Entries,
        Dictionary<string, List<FileEntry>> ByTag);
    private sealed record FileEntry(
        string Path,
        string[] Tags,
        string[] Topics,
        string[] Roles,
        bool Ssot,
        string Preview);
}
