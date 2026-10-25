namespace AgentNotes.Okf;

/// <summary>
/// Inserts or replaces the OKF frontmatter block at the start of a canon document (KB ADR 026, M1 pilot).
/// Preserves a leading BOM and the rest of the document verbatim. Total for any input string.
/// </summary>
public static class OkfMigrator
{
    private const string Fence = "---";

    /// <summary>
    /// Returns <paramref name="markdown"/> with <paramref name="frontmatter"/> as its leading
    /// frontmatter block. An existing leading block (opening and closing <c>---</c> fence lines)
    /// is replaced; a document without one gets the block prepended. Idempotent for equal inputs.
    /// An unterminated leading fence is not treated as frontmatter (the block is prepended instead).
    /// </summary>
    public static string EnsureFrontmatter(string markdown, OkfFrontmatter frontmatter)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(frontmatter);

        var bom = markdown.StartsWith('\uFEFF') ? "\uFEFF" : string.Empty;
        var body = bom.Length == 0 ? markdown : markdown[1..];

        var cut = FindExistingBlockEnd(body);
        var block = Fence + "\n" + frontmatter.ToYaml() + Fence + "\n";
        return bom + block + body[cut..];
    }

    /// <summary>Returns the index just past a complete leading frontmatter block, or 0 when absent.</summary>
    private static int FindExistingBlockEnd(string body)
    {
        var firstLineEnd = body.IndexOf('\n');
        var firstLine = (firstLineEnd < 0 ? body : body[..firstLineEnd]).TrimEnd('\r');
        if (firstLine != Fence)
        {
            return 0;
        }

        var pos = firstLineEnd < 0 ? body.Length : firstLineEnd + 1;
        while (pos < body.Length)
        {
            var nl = body.IndexOf('\n', pos);
            var line = (nl < 0 ? body[pos..] : body[pos..nl]).TrimEnd('\r');
            if (line == Fence)
            {
                return nl < 0 ? body.Length : nl + 1;
            }

            if (nl < 0)
            {
                return 0;
            }

            pos = nl + 1;
        }

        return 0;
    }
}
