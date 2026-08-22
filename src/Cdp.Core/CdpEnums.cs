namespace Cdp.Core;

public enum CdpPhase
{
    /// <summary>Pull known memory into context (canon, hot, pack cards) — not discovery.</summary>
    Recall,
    Explore,
    Clarify,
    /// <summary>Before act: critique brief, scout reuse, name variants/rejects — not mutate.</summary>
    Plan,
    Act,
    Verify,
    /// <summary>Judgment after verify, before ship — blast radius, intent match, not machine green.</summary>
    Review,
    Handoff
}

public enum CdpObjectKind
{
    Kb,
    Code,
    Repo,
    Task,
    Finding,
    Process,
    Issue,
    Session
}

public enum CdpIntent
{
    Find,
    Cite,
    Change,
    Verify,
    Record,
    Ship
}

public static class CdpEnumParse
{
    public static bool TryParsePhase(string? raw, out CdpPhase phase) =>
        Enum.TryParse(Normalize(raw), ignoreCase: true, out phase);

    public static bool TryParseObject(string? raw, out CdpObjectKind obj) =>
        Enum.TryParse(Normalize(raw), ignoreCase: true, out obj);

    public static bool TryParseIntent(string? raw, out CdpIntent intent) =>
        Enum.TryParse(Normalize(raw), ignoreCase: true, out intent);

    /// <summary>
    /// Prefer <see cref="LanguageRegistry.TryNormalize"/> from host config.
    /// Fallback uses <see cref="LanguageRegistry.Default"/>.
    /// </summary>
    public static bool TryParseLanguage(string? raw, out string language) =>
        LanguageRegistry.Default.TryNormalize(raw, out language);

    public static string ToWire(CdpPhase p) => p.ToString().ToLowerInvariant();
    public static string ToWire(CdpObjectKind o) => o.ToString().ToLowerInvariant();
    public static string ToWire(CdpIntent i) => i.ToString().ToLowerInvariant();

    private static string Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? "" : raw.Trim();
}
