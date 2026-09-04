#nullable enable

using AIGuiders.Platform.Execution.LanguageIntelligence.Anchors;
using AIGuiders.Platform.IntermediateRepresentation.Language;

namespace Cdp.ScriptableIde;

/// <summary>
/// CDP compatibility façade — SSOT: <see cref="BracketAnchorWire"/> / <see cref="BracketAnchorSpan"/> (platform).
/// </summary>
public static class BracketLocate
{
    public enum AxisFamily
    {
        None = 0,
        Csharp = 1,
        Xml = 2,
        Navigation = 3,
        Fsharp = 4,
    }

    public sealed record Span(
        string? File,
        string? MemberKey,
        int? LineStart,
        int? LineEnd,
        string? ScopeKind = null,
        int? ScopeIndex = null,
        string? Role = null,
        string? XmlPath = null,
        string? Attr = null,
        string? Family = null,
        string? Command = null,
        string? Go = null,
        Span? NestedAnchor = null,
        string? TextNeedle = null)
    {
        internal BracketAnchorSpan ToPlatform() => new(
            File,
            MemberKey,
            LineStart,
            LineEnd,
            ScopeKind,
            ScopeIndex,
            Role,
            XmlPath,
            Attr,
            Family,
            Command,
            Go,
            NestedAnchor?.ToPlatform(),
            TextNeedle);

        internal static Span FromPlatform(BracketAnchorSpan span) => new(
            span.File,
            span.MemberKey,
            span.LineStart,
            span.LineEnd,
            span.ScopeKind,
            span.ScopeIndex,
            span.Role,
            span.XmlPath,
            span.Attr,
            span.Family,
            span.Command,
            span.Go,
            span.NestedAnchor is null ? null : FromPlatform(span.NestedAnchor),
            span.TextNeedle);
    }

    public static Span Parse(string bracketOrInner) =>
        Span.FromPlatform(BracketAnchorWire.Parse(bracketOrInner));

    public static AxisFamily ClassifyFamily(Span span, out string? error)
    {
        var family = BracketAnchorWire.ClassifyFamily(span.ToPlatform(), out error);
        return (AxisFamily)(int)family;
    }

    public static string Format(Span span, bool preferCanonical = false) =>
        BracketAnchorWire.Format(span.ToPlatform(), preferCanonical);

    public static string SanitizeTextNeedle(string? raw) =>
        BracketAnchorWire.SanitizeTextNeedle(raw);
}
