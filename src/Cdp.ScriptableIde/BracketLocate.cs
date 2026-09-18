#nullable enable

using AIGuiders.Platform.Execution.LanguageIntelligence;
using AIGuiders.Platform.Execution.LanguageIntelligence.Relations;

namespace Cdp.ScriptableIde;

/// <summary>
/// CDP compatibility façade — Kind-first parse via <see cref="BracketResolveBoundary"/> with legacy F/M/L fallback via <see cref="RelationWireBoundary"/>.
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
        Json = 5,
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
        string? TextNeedle = null,
        string? TypeKey = null)
    {
        internal LegacyWireSpan ToLegacyWire() => new(
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
            NestedAnchor?.ToLegacyWire(),
            TextNeedle,
            TypeKey);

        internal static Span FromLegacyWire(LegacyWireSpan span) => new(
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
            span.NestedAnchor is null ? null : FromLegacyWire(span.NestedAnchor),
            span.TextNeedle,
            span.TypeKey);

        internal static Span FromAxes(CodeEditResolveAxes axes) => new(
            axes.File,
            axes.MemberKey,
            axes.LineStart,
            axes.LineEnd,
            axes.ScopeKind,
            axes.ScopeIndex,
            axes.Role,
            axes.XmlPath,
            axes.Attr,
            Family: null,
            Command: null,
            Go: null,
            NestedAnchor: null,
            axes.TextNeedle,
            axes.TypeKey);
    }

    public static Span Parse(string bracketOrInner)
    {
        if (BracketResolveBoundary.TryParseToAxes(bracketOrInner, out var axes, out _))
            return Span.FromAxes(axes);
        return Span.FromLegacyWire(RelationWireBoundary.Parse(bracketOrInner));
    }

    public static AxisFamily ClassifyFamily(Span span, out string? error)
    {
        var family = RelationWireBoundary.ClassifyFamily(span.ToLegacyWire(), out error);
        return (AxisFamily)(int)family;
    }

    public static string Format(Span span, bool preferCanonical = false)
    {
        if (preferCanonical && BracketResolveBoundary.TryFormatCodeEdit(span.ToLegacyWire(), out var kindWire))
            return kindWire;
        return RelationWireBoundary.Format(span.ToLegacyWire(), preferCanonical);
    }

    public static string SanitizeTextNeedle(string? raw) =>
        RelationWireBoundary.SanitizeTextNeedle(raw);
}
