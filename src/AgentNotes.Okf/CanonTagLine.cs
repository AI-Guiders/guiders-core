namespace AgentNotes.Okf;

/// <summary>
/// Parser and renderer for the canon tag line convention: <c>**Tags:** #a #b</c>.
/// Maps to the OKF frontmatter <c>tags</c> field (KB ADR 026 §2).
/// </summary>
public static class CanonTagLine
{
    private const string Prefix = "**Tags:**";

    /// <summary>
    /// Parses a canon tag line. Total: never throws for any input.
    /// Returns <see langword="true"/> only when the line starts with the
    /// <c>**Tags:**</c> prefix and yields at least one non-empty tag.
    /// Tokens without a leading <c>#</c> are ignored.
    /// </summary>
    public static bool TryParse(string? line, out IReadOnlyList<string> tags)
    {
        tags = [];
        if (line is null)
        {
            return false;
        }

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parsed = new List<string>();
        foreach (var token in trimmed[Prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 1 && token[0] == '#')
            {
                parsed.Add(token[1..]);
            }
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        tags = parsed;
        return true;
    }

    /// <summary>
    /// Renders <paramref name="tags"/> back into the canon tag line form.
    /// Empty tags are skipped; an empty list renders the bare prefix.
    /// </summary>
    public static string Render(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var list = tags.Where(t => t.Length > 0).Select(t => "#" + t).ToList();
        return list.Count == 0 ? Prefix : Prefix + " " + string.Join(" ", list);
    }
}
