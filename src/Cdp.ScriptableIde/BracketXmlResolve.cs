using AIGuiders.Platform.Execution.Language.Xml.Anchors;
using AIGuiders.Platform.Execution.LanguageIntelligence.Anchors;

namespace Cdp.ScriptableIde;

/// <summary>CDP compatibility façade — SSOT: <see cref="XmlBracketAnchorResolve"/>.</summary>
public static class BracketXmlResolve
{
    public sealed record TextRange(int LineStart, int ColumnStart, int LineEnd, int ColumnEnd);

    public sealed record ResolveResult(
        TextRange Range,
        string Detail,
        bool Insert,
        string? InsertElementName,
        string? InsertIndent);

    public static bool TryResolve(
        string absoluteFilePath,
        string? sourceText,
        BracketLocate.Span span,
        out ResolveResult result,
        out string detail)
    {
        var ok = XmlBracketAnchorResolve.TryResolve(
            absoluteFilePath,
            sourceText,
            span.ToPlatform(),
            out var platform,
            out detail);
        if (!ok)
        {
            result = default!;
            return false;
        }

        result = new ResolveResult(
            new TextRange(
                platform.Range.LineStart,
                platform.Range.ColumnStart,
                platform.Range.LineEnd,
                platform.Range.ColumnEnd),
            platform.Detail,
            platform.Insert,
            platform.InsertElementName,
            platform.InsertIndent);
        detail = platform.Detail;
        return true;
    }

    public static string BuildInsertElement(string elementName, string innerText, string indent) =>
        XmlBracketAnchorResolve.BuildInsertElement(elementName, innerText, indent);

    public static void OffsetToLineCol(string text, int offset, out int line, out int column) =>
        XmlBracketAnchorResolve.OffsetToLineCol(text, offset, out line, out column);
}
