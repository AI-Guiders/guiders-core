namespace Cdp.Core;

/// <summary>
/// Shortlist = f(phase, object [, language]); intent ranks.
/// ListTools budget is intentionally small — palette, not union of all backends.
/// </summary>
public static class PhaseObjectCatalog
{
    /// <summary>Hard default for host ListTools domain slots (meta tools are separate).</summary>
    public const int DefaultListToolsLimit = 10;

    /// <summary>Preview / cdp_tools may ask for more; still capped.</summary>
    public const int MaxQueryLimit = 40;

    public static IReadOnlyList<CatalogHit> Query(
        IEnumerable<ToolAffordance> all,
        CdpPhase phase,
        CdpObjectKind obj,
        CdpIntent? intent = null,
        int limit = DefaultListToolsLimit,
        string? language = null,
        bool dedupeByUnderlying = true)
    {
        var lim = Math.Clamp(limit, 1, MaxQueryLimit);
        var hits = new List<CatalogHit>();
        var langFilter = CdpLanguages.IsAny(language) ? null : language!.Trim().ToLowerInvariant();

        foreach (var a in all)
        {
            if (!a.Phases.Contains(phase))
                continue;
            if (!a.Objects.Contains(obj))
                continue;

            var langs = a.EffectiveLanguages;
            if (langFilter is { } wantLang)
            {
                if (!langs.Any(l => l.Equals(CdpLanguages.Any, StringComparison.OrdinalIgnoreCase))
                    && !langs.Any(l => l.Equals(wantLang, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            var score = 100 - a.Cost * 3 - a.Risk * 5;
            score += FacetPreferenceBoost(a.Domain, obj);

            if (intent is { } want)
            {
                if (a.Intents.Count > 0 && !a.Intents.Contains(want))
                    score -= 40;
                else if (a.Intents.Contains(want))
                    score += 25;
            }

            if (langFilter is { } wl)
            {
                if (langs.Any(l => l.Equals(wl, StringComparison.OrdinalIgnoreCase)))
                    score += 15;
            }

            // Bare IDE verbs cover find; legacy roslyn_* / debug noise should not own explore+code palette.
            if (a.Hint is { } hint
                && hint.StartsWith("legacy", StringComparison.OrdinalIgnoreCase))
                score -= 55;

            if (obj == CdpObjectKind.Code && intent == CdpIntent.Find)
            {
                if (a.Domain == CdpDomains.Debug)
                    score -= 35;
                if (a.Domain == CdpDomains.CodebaseIndex
                    && a.UnderlyingName.Contains("search", StringComparison.Ordinal))
                    score += 25;
            }

            hits.Add(new CatalogHit(a, score));
        }

        IEnumerable<CatalogHit> ordered = hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => FacetPreference(h.Affordance.Domain, obj))
            .ThenBy(h => h.Affordance.PrefixedName, StringComparer.Ordinal);

        if (dedupeByUnderlying)
        {
            // One wire underlying in the palette — avoids memory_project_* crowding out world packs.
            // Exception: write/append/upsert keep world + project (different allowed_roots;
            // Kolb JOURNAL lives under work/projects → memory_project_append_*).
            ordered = ordered
                .GroupBy(h => h.Affordance.UnderlyingName, StringComparer.Ordinal)
                .SelectMany(DedupeUnderlyingGroup);
            ordered = ordered
                .OrderByDescending(h => h.Score)
                .ThenByDescending(h => FacetPreference(h.Affordance.Domain, obj))
                .ThenBy(h => h.Affordance.PrefixedName, StringComparer.Ordinal);
        }

        return ordered.Take(lim).ToList();
    }

    private static readonly HashSet<string> MultiFacetWriteUnderlyings = new(StringComparer.Ordinal)
    {
        "append_knowledge_file",
        "write_knowledge_file",
        "upsert_knowledge_section",
    };

    private static IEnumerable<CatalogHit> DedupeUnderlyingGroup(IGrouping<string, CatalogHit> g)
    {
        if (!MultiFacetWriteUnderlyings.Contains(g.Key))
            return [g.First()];

        // Prefer world + project only (skill writes are rare; keep palette tight).
        return g
            .Where(h => h.Affordance.Domain is CdpDomains.MemoryWorld or CdpDomains.MemoryProject)
            .GroupBy(h => h.Affordance.Domain, StringComparer.Ordinal)
            .Select(dg => dg.First());
    }

    /// <summary>Cold ListTools = recall known KB (Cite ranks pack cards / tags).</summary>
    public static IReadOnlyList<ToolAffordance> DefaultColdStart(IEnumerable<ToolAffordance> all) =>
        Query(all, CdpPhase.Recall, CdpObjectKind.Kb, CdpIntent.Cite, limit: DefaultListToolsLimit)
            .Select(h => h.Affordance)
            .ToList();

    /// <summary>Kb → prefer world packs; Session → session; else neutral.</summary>
    internal static int FacetPreference(string domain, CdpObjectKind obj) => obj switch
    {
        CdpObjectKind.Kb => domain switch
        {
            CdpDomains.MemoryWorld => 30,
            CdpDomains.MemorySkill => 10,
            // Kolb / work+personal parks — keep competitive with world so multi-facet writes survive Take(limit)
            CdpDomains.MemoryProject => 22,
            CdpDomains.MemorySession => 5,
            _ => 0
        },
        CdpObjectKind.Session => domain == CdpDomains.MemorySession ? 30 : 0,
        CdpObjectKind.Task => domain == CdpDomains.MemoryTask ? 30 : 0,
        CdpObjectKind.Repo => domain == CdpDomains.Git ? 28 : 0,
        CdpObjectKind.Code => domain == CdpDomains.Git ? 12 : 0,
        _ => 0
    };

    private static int FacetPreferenceBoost(string domain, CdpObjectKind obj) =>
        FacetPreference(domain, obj) / 2; // mild score nudge before dedupe
}
