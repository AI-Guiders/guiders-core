#nullable enable

namespace Cdp.ScriptableIde;

/// <summary>
/// Content-needle sniper for anchor wires: [F:file;T:needle] (T:-only, no M/S/L/X/A axes).
/// Locates the UNIQUE line containing the needle and returns the full-line corridor
/// (EditSniper parity). Language-agnostic — the consumer (DocumentAnchorEdit) routes
/// here when the buffer language has no structural resolver for T: (xml, md, text…).
/// </summary>
public static class BracketTextNeedleResolve
{
    public sealed record ResolveResult(int LineStart, int LineEnd, string LineText, string Detail);

    public static bool TryResolve(
        string absoluteFilePath,
        string? sourceText,
        BracketLocate.Span span,
        out ResolveResult result,
        out string detail)
    {
        result = default!;
        detail = "";

        var needle = span.TextNeedle;
        if (string.IsNullOrWhiteSpace(needle))
        {
            detail = "need_T";
            return false;
        }

        if (span.MemberKey is { Length: > 0 }
            || span.ScopeKind is { Length: > 0 }
            || span.LineStart is not null
            || span.XmlPath is { Length: > 0 }
            || span.Attr is { Length: > 0 })
        {
            detail = "T_only_sniper_no_other_axes";
            return false;
        }

        string text;
        if (sourceText is not null)
            text = sourceText;
        else if (File.Exists(absoluteFilePath))
            text = File.ReadAllText(absoluteFilePath);
        else
        {
            detail = "file_missing";
            return false;
        }

        needle = needle.Trim();
        var hits = new List<(int Line, int Start, int End)>();
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == '\n')
            {
                var lineEnd = i > 0 && text[i - 1] == '\r' ? i - 1 : i;
                if (lineEnd >= lineStart)
                {
                    var idx = text.IndexOf(needle, lineStart, lineEnd - lineStart, StringComparison.Ordinal);
                    if (idx >= 0)
                        hits.Add((line, lineStart, lineEnd));
                }

                line++;
                lineStart = i + 1;
            }
        }

        if (hits.Count == 0)
        {
            detail = "needle_not_found";
            return false;
        }

        if (hits.Count > 1)
        {
            detail = $"needle_ambiguous:{hits.Count}";
            return false;
        }

        var (hitLine, hs, he) = hits[0];
        result = new ResolveResult(hitLine, hitLine, text.Substring(hs, he - hs), "line_needle");
        detail = result.Detail;
        return true;
    }
}