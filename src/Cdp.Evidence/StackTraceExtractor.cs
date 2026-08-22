using System.Text.RegularExpressions;

namespace Cdp.Evidence;

/// <summary>
/// .NET / CLR stack frames and "in file:line N" loci from test failure bodies.
/// </summary>
public static partial class StackTraceExtractor
{
    // at Foo.Bar() in C:\path\File.cs:line 42  (drive letter OK — stop at ":line")
    [GeneratedRegex(
        @"\bin\s+(?<file>.+?\.(?:cs|fs|vb|ts|tsx|js|py))\s*:\s*line\s+(?<line>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InFileLine();

    // Failed tests console: sometimes bare path(line): message mixed in
    public static IEnumerable<EvidenceItem> Extract(string text, EvidenceContext ctx, string severity = "error")
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in InFileLine().Matches(text))
        {
            var path = MsBuildExtractor.NormalizePath(m.Groups["file"].Value.Trim().Trim('"'), ctx);
            var lineNum = int.Parse(m.Groups["line"].ValueSpan);
            var key = path + ":" + lineNum;
            if (!seen.Add(key)) continue;

            yield return new EvidenceItem(
                Severity: severity,
                Message: m.Value.Trim(),
                Path: path,
                Line: lineNum,
                Anchor: LocusWire.TryFileLine(path, lineNum),
                Hint: "Stack/assert locus — open anchor, don't re-scan the log.",
                Title: Path.GetFileName(path));
        }
    }

    /// <summary>One failed test → prefer first stack locus in message, else title-only item.</summary>
    public static EvidenceItem FromFailedTest(string name, string? message, EvidenceContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            var loci = Extract(message, ctx).ToList();
            if (loci.Count > 0)
            {
                var first = loci[0];
                return first with
                {
                    Id = name,
                    Title = name,
                    Message = message!.Trim(),
                    Hint = first.Hint ?? "Failed test — open anchor at assert/stack."
                };
            }
        }

        return new EvidenceItem(
            Severity: "error",
            Message: string.IsNullOrWhiteSpace(message) ? $"Failed: {name}" : message.Trim(),
            Id: name,
            Title: name,
            Hint: "No file locus in message — re-run with detailed logger or open test by name.");
    }
}
