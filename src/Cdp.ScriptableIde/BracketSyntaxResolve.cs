#nullable enable

using AIGuiders.Platform.Execution.Language.CSharp.Anchors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cdp.ScriptableIde;

/// <summary>
/// CDP compatibility façade — SSOT: <see cref="CSharpBracketAnchorResolve"/> (Language.CSharp.Anchors).
/// </summary>
public static class BracketSyntaxResolve
{
    public sealed record TextRange(int LineStart, int ColumnStart, int LineEnd, int ColumnEnd)
    {
        internal static TextRange From(CSharpBracketAnchorResolve.TextRange range) =>
            new(range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd);

        internal CSharpBracketAnchorResolve.TextRange ToPlatform() =>
            new(LineStart, ColumnStart, LineEnd, ColumnEnd);
    }

    public sealed record AttachTarget(
        string AbsolutePath,
        SyntaxTree Tree,
        CompilationUnitSyntax Root,
        SyntaxNode Node,
        string Detail)
    {
        internal static AttachTarget From(CSharpBracketAnchorResolve.AttachTarget target) =>
            new(target.AbsolutePath, target.Tree, target.Root, target.Node, target.Detail);
    }

    public static bool TryResolve(string absoluteFilePath, BracketLocate.Span span, out TextRange range, out string detail)
    {
        var ok = CSharpBracketAnchorResolve.TryResolve(absoluteFilePath, span.ToPlatform(), out var platformRange, out detail);
        range = ok ? TextRange.From(platformRange) : default!;
        return ok;
    }

    public static bool TryResolve(
        string absoluteFilePath,
        string? sourceText,
        BracketLocate.Span span,
        out TextRange range,
        out string detail)
    {
        var ok = CSharpBracketAnchorResolve.TryResolve(
            absoluteFilePath,
            sourceText,
            span.ToPlatform(),
            out var platformRange,
            out detail);
        range = ok ? TextRange.From(platformRange) : default!;
        return ok;
    }

    public static bool TryFindAttachTarget(
        string absoluteFilePath,
        BracketLocate.Span span,
        out AttachTarget target,
        out string detail)
    {
        var ok = CSharpBracketAnchorResolve.TryFindAttachTarget(
            absoluteFilePath,
            span.ToPlatform(),
            out var platformTarget,
            out detail);
        target = ok ? AttachTarget.From(platformTarget) : default!;
        return ok;
    }

    public static bool TryFindAttachTarget(
        string absoluteFilePath,
        string? sourceText,
        BracketLocate.Span span,
        out AttachTarget target,
        out string detail)
    {
        var ok = CSharpBracketAnchorResolve.TryFindAttachTarget(
            absoluteFilePath,
            sourceText,
            span.ToPlatform(),
            out var platformTarget,
            out detail);
        target = ok ? AttachTarget.From(platformTarget) : default!;
        return ok;
    }

    public static bool TryNarrowRangeToTextNeedle(
        SyntaxTree tree,
        SyntaxNode scope,
        string needleRaw,
        out TextRange range,
        out string detail)
    {
        var ok = CSharpBracketAnchorResolve.TryNarrowRangeToTextNeedle(
            tree,
            scope,
            needleRaw,
            out var platformRange,
            out detail);
        range = ok ? TextRange.From(platformRange) : default!;
        return ok;
    }

    public static bool TryGetBlockInteriorInsertPoint(
        SyntaxNode node,
        bool before,
        out TextRange point,
        out string detail)
    {
        var ok = CSharpBracketAnchorResolve.TryGetBlockInteriorInsertPoint(
            node,
            before,
            out var platformPoint,
            out detail);
        point = ok ? TextRange.From(platformPoint) : default!;
        return ok;
    }
}
