#nullable enable

using AIGuiders.Platform.Execution.LanguageIntelligence;
using AIGuiders.Platform.Execution.LanguageIntelligence.Relations;

namespace Cdp.ScriptableIde;

/// <summary>
/// CDP compatibility façade — Kind-first parse via <see cref="BracketResolveBoundary"/> with doc-scan F/M/L fallback.
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

        internal static Span FromNavAxes(NavResolveAxes axes) => new(
            axes.File,
            MemberKey: axes.Member,
            LineStart: axes.Line,
            LineEnd: axes.Line,
            Family: "navigation",
            Command: axes.Command,
            Go: axes.Go);

        internal WireFamilyClassifier.Probe ToFamilyProbe() => new(
            File,
            MemberKey,
            LineStart,
            ScopeKind,
            Role,
            XmlPath,
            Attr,
            Family,
            Command,
            Go,
            NestedAnchor?.ToFamilyProbe(),
            TextNeedle,
            TypeKey);

        internal CodeEditResolveAxes ToCodeEditAxes() => new(
            File,
            MemberKey,
            LineStart,
            LineEnd,
            ScopeKind,
            ScopeIndex,
            Role,
            XmlPath,
            Attr,
            TextNeedle,
            TypeKey);

        internal NavResolveAxes ToNavAxes() => new(
            File,
            LineStart,
            Column: null,
            Command,
            Go,
            Solution: null,
            Member: MemberKey);
    }

    public static Span Parse(string bracketOrInner)
    {
        if (BracketResolveBoundary.TryParseToAxes(bracketOrInner, out var axes, out _))
            return Span.FromAxes(axes);
        if (BracketResolveBoundary.TryParseNav(bracketOrInner, out var nav, out _))
            return Span.FromNavAxes(nav);

        throw new ArgumentException("unsupported_wire");
    }

    public static AxisFamily ClassifyFamily(Span span, out string? error)
    {
        var family = WireFamilyClassifier.Classify(span.ToFamilyProbe(), out error);
        return (AxisFamily)(int)family;
    }

    public static string Format(Span span, bool preferCanonical = false)
    {
        _ = preferCanonical;
        if (ClassifyFamily(span, out _) == AxisFamily.Navigation)
        {
            if (BracketResolveBoundary.TryFormatNav(span.ToNavAxes(), out var navWire))
                return navWire;
        }

        if (BracketResolveBoundary.TryFormatCodeEdit(span.ToCodeEditAxes(), out var kindWire))
            return kindWire;

        throw new ArgumentException("unsupported_wire");
    }

    public static string SanitizeTextNeedle(string? raw) =>
        RelationWireBoundary.SanitizeTextNeedle(raw);
}
