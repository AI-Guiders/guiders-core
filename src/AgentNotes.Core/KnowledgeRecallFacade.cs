using System.Text.Json;

namespace AgentNotes.Core;

/// <summary>CDP-ADR-0218: unified KB recall — corpus (resolve/lookup/search) then hot notes.</summary>
public static class KnowledgeRecallFacade
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    internal sealed record UnifiedHit(
        string Layer,
        string Kind,
        string? Path,
        int? Line,
        string? Preview,
        int ScopeBoost,
        string? Text);

    public static string Recall(
        NotesStorage storage,
        string query,
        string? layer = null,
        int limit = 15,
        string? activeScope = null,
        string? primaryProjectId = null,
        bool scopeOnly = false,
        string? workspacePath = null)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            throw new ArgumentException("query is required.");

        var layerRequested = NormalizeLayer(layer);
        var lim = Math.Clamp(limit, 1, 50);
        var layersUsed = new List<string>();
        var hits = new List<UnifiedHit>();
        JsonElement? hotBlock = null;

        if (layerRequested is "auto" or "corpus")
        {
            if (TryCorpusResolve(storage, q, lim, activeScope, primaryProjectId, scopeOnly, layersUsed, hits))
                return Serialize(q, layerRequested, layersUsed, hits, null);

            if (TryCorpusLookup(storage, q, lim, activeScope, primaryProjectId, scopeOnly, layersUsed, hits))
                return Serialize(q, layerRequested, layersUsed, hits, null);

            if (TryCorpusSearch(storage, q, lim, activeScope, primaryProjectId, scopeOnly, layersUsed, hits))
                return Serialize(q, layerRequested, layersUsed, hits, null);
        }

        if (layerRequested is "auto" or "hot")
        {
            layersUsed.Add("hot");
            var hotJson = storage.Search(workspacePath ?? "", q, lim);
            using var hotDoc = JsonDocument.Parse(hotJson);
            hotBlock = hotDoc.RootElement.Clone();
            AppendHotHits(hotDoc.RootElement, hits, lim);
        }

        return Serialize(q, layerRequested, layersUsed, hits, hotBlock);
    }

    internal static string NormalizeLayer(string? layer)
    {
        var l = (layer ?? "auto").Trim().ToLowerInvariant();
        return l switch
        {
            "" or "auto" => "auto",
            "corpus" or "kb" or "tags" => "corpus",
            "hot" or "session" or "notes" => "hot",
            _ => throw new ArgumentException("layer must be auto|corpus|hot.")
        };
    }

    static bool TryCorpusResolve(
        NotesStorage storage,
        string query,
        int limit,
        string? activeScope,
        string? primaryProjectId,
        bool scopeOnly,
        List<string> layersUsed,
        List<UnifiedHit> hits)
    {
        var resolveJson = storage.QueryKnowledgeTags(
            null,
            query: query,
            mode: "resolve",
            limit: limit,
            activeScope: activeScope,
            primaryProjectId: primaryProjectId,
            scopeOnly: scopeOnly);
        if (!TryGetResolvedTag(resolveJson, out var resolvedTag))
            return false;

        var lookupJson = storage.QueryKnowledgeTags(
            null,
            query: resolvedTag,
            mode: "lookup",
            limit: limit,
            activeScope: activeScope,
            primaryProjectId: primaryProjectId,
            scopeOnly: scopeOnly);
        var parsed = ParseCorpusHits(lookupJson, "resolve");
        if (parsed.Count == 0)
            return false;

        layersUsed.Add("corpus");
        layersUsed.Add("resolve");
        hits.AddRange(parsed);
        return true;
    }

    static bool TryCorpusLookup(
        NotesStorage storage,
        string query,
        int limit,
        string? activeScope,
        string? primaryProjectId,
        bool scopeOnly,
        List<string> layersUsed,
        List<UnifiedHit> hits)
    {
        var lookupJson = storage.QueryKnowledgeTags(
            null,
            query: query,
            mode: "lookup",
            limit: limit,
            activeScope: activeScope,
            primaryProjectId: primaryProjectId,
            scopeOnly: scopeOnly);
        var parsed = ParseCorpusHits(lookupJson, "lookup");
        if (parsed.Count == 0)
            return false;

        layersUsed.Add("corpus");
        layersUsed.Add("lookup");
        hits.AddRange(parsed);
        return true;
    }

    static bool TryCorpusSearch(
        NotesStorage storage,
        string query,
        int limit,
        string? activeScope,
        string? primaryProjectId,
        bool scopeOnly,
        List<string> layersUsed,
        List<UnifiedHit> hits)
    {
        var searchJson = storage.QueryKnowledgeTags(
            null,
            query: query,
            mode: "search",
            limit: limit,
            activeScope: activeScope,
            primaryProjectId: primaryProjectId,
            scopeOnly: scopeOnly);
        var parsed = ParseCorpusHits(searchJson, "search");
        if (parsed.Count == 0)
            return false;

        layersUsed.Add("corpus");
        layersUsed.Add("search");
        hits.AddRange(parsed);
        return true;
    }

    static List<UnifiedHit> ParseCorpusHits(string json, string kind)
    {
        var result = new List<UnifiedHit>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var h in hits.EnumerateArray())
            {
                var path = h.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;
                int? line = h.TryGetProperty("line", out var ln) && ln.ValueKind == JsonValueKind.Number && ln.TryGetInt32(out var lv)
                    ? lv
                    : null;
                var preview = h.TryGetProperty("preview", out var pr) && pr.ValueKind == JsonValueKind.String
                    ? pr.GetString()
                    : null;
                var boost = h.TryGetProperty("scope_boost", out var sb) && sb.TryGetInt32(out var bv) ? bv : 0;
                result.Add(new UnifiedHit("corpus", kind, path, line, preview, boost, null));
            }
        }
        catch
        {
            /* best effort */
        }

        return result;
    }

    static void AppendHotHits(JsonElement hotRoot, List<UnifiedHit> hits, int limit)
    {
        if (!hotRoot.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
            return;

        foreach (var m in matches.EnumerateArray())
        {
            if (hits.Count(h => h.Layer == "hot") >= limit)
                break;
            int? line = m.TryGetProperty("line", out var ln) && ln.TryGetInt32(out var lv) ? lv : null;
            var text = m.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            hits.Add(new UnifiedHit("hot", "grep", null, line, null, 0, text));
        }
    }

    static bool TryGetResolvedTag(string resolveJson, out string resolvedTag)
    {
        resolvedTag = "";
        try
        {
            using var doc = JsonDocument.Parse(resolveJson);
            if (!doc.RootElement.TryGetProperty("known", out var known) || known.ValueKind != JsonValueKind.True)
                return false;
            if (!doc.RootElement.TryGetProperty("resolved_tag", out var tagEl)
                || tagEl.ValueKind != JsonValueKind.String)
                return false;
            var raw = tagEl.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            resolvedTag = raw.Trim().TrimStart('#');
            return resolvedTag.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static string Serialize(
        string query,
        string layerRequested,
        IReadOnlyList<string> layersUsed,
        IReadOnlyList<UnifiedHit> hits,
        JsonElement? hotBlock)
    {
        var payload = new
        {
            tool = "recall_knowledge",
            query,
            layer_requested = layerRequested,
            layers_used = layersUsed,
            total = hits.Count,
            hits = hits.Select(h => new
            {
                layer = h.Layer,
                kind = h.Kind,
                path = h.Path,
                line = h.Line,
                preview = h.Preview,
                text = h.Text,
                scope_boost = h.ScopeBoost
            }).ToArray(),
            hot = hotBlock.HasValue ? JsonSerializer.Deserialize<object>(hotBlock.Value.GetRawText()) : null
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    internal static int CountCorpusHits(string json) => ParseCorpusHits(json, "lookup").Count;
}
