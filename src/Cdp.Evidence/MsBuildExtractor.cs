using System.Text.RegularExpressions;

namespace Cdp.Evidence;

/// <summary>MSBuild / dotnet build lines: path(line[,col]): error|warning CODE: message</summary>
public static partial class MsBuildExtractor
{
    [GeneratedRegex(
        @"^(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?<sev>error|warning)\s*(?<code>\S*?)?\s*:\s*(?<msg>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticLine();

    public static IEnumerable<EvidenceItem> Extract(string text, EvidenceContext ctx)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            var m = DiagnosticLine().Match(line);
            if (!m.Success) continue;

            var path = NormalizePath(m.Groups["file"].Value.Trim().Trim('"'), ctx);
            var lineNum = int.Parse(m.Groups["line"].ValueSpan);
            int? col = m.Groups["col"].Success && m.Groups["col"].Value.Length > 0
                ? int.Parse(m.Groups["col"].ValueSpan)
                : null;
            var sev = m.Groups["sev"].Value.Equals("warning", StringComparison.OrdinalIgnoreCase)
                ? "warning"
                : "error";
            var code = m.Groups["code"].Success ? m.Groups["code"].Value.Trim() : null;
            if (string.IsNullOrWhiteSpace(code)) code = null;
            var msg = m.Groups["msg"].Value.Trim();

            yield return new EvidenceItem(
                Severity: sev,
                Message: msg,
                Id: code,
                Path: path,
                Line: lineNum,
                Column: col,
                Anchor: LocusWire.TryFileLine(path, lineNum, col),
                Hint: EvidenceHints.ForCode(code, msg),
                Title: code);
        }
    }

    internal static string NormalizePath(string path, EvidenceContext ctx)
    {
        path = path.Replace('/', Path.DirectorySeparatorChar);
        if (ctx.RemapPath is { } remap)
            path = remap(path);
        if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(ctx.ProjectRoot))
        {
            var combined = Path.GetFullPath(Path.Combine(ctx.ProjectRoot!, path));
            return combined;
        }

        try
        {
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);
        }
        catch
        {
            /* keep as-is */
        }

        return path;
    }
}
