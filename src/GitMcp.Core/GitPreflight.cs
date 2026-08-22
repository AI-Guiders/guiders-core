namespace GitMcp.Core;

/// <summary>
/// Lightweight classification of changed files into semantic and noise buckets.
/// </summary>
public static class GitPreflight
{
    public sealed record Report(
        IReadOnlyList<string> ChangedFiles,
        IReadOnlyList<string> UntrackedFiles,
        IReadOnlyList<string> SemanticFiles,
        IReadOnlyList<string> WhitespaceOnlyFiles,
        IReadOnlyList<string> EolOnlyFiles,
        IReadOnlyList<string> BomOnlyFiles,
        IReadOnlyList<string> SuggestedSafeFixCommands);

    public static Report BuildReport(
        IEnumerable<string> changed,
        IEnumerable<string> untracked,
        IEnumerable<string> ignoreCrAtEol,
        IEnumerable<string> ignoreWhitespace,
        IReadOnlyDictionary<string, string>? patchesByFile = null)
    {
        var changedSet = ToOrderedSet(changed);
        var untrackedSet = ToOrderedSet(untracked);
        var ignoreCrSet = ToOrderedSet(ignoreCrAtEol);
        var ignoreWsSet = ToOrderedSet(ignoreWhitespace);

        var semantic = new SortedSet<string>(StringComparer.Ordinal);
        var whitespaceOnly = new SortedSet<string>(StringComparer.Ordinal);
        var eolOnly = new SortedSet<string>(StringComparer.Ordinal);
        var bomOnly = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in changedSet)
        {
            if (!ignoreCrSet.Contains(path))
            {
                eolOnly.Add(path);
                continue;
            }

            if (!ignoreWsSet.Contains(path))
            {
                whitespaceOnly.Add(path);
                continue;
            }

            semantic.Add(path);
        }

        if (patchesByFile is not null)
        {
            foreach (var path in semantic.ToArray())
            {
                if (patchesByFile.TryGetValue(path, out var patch) && IsBomOnlyPatch(patch))
                {
                    bomOnly.Add(path);
                    semantic.Remove(path);
                }
            }
        }

        var safeFixCommands = new List<string>();
        if (eolOnly.Count > 0)
            safeFixCommands.Add("git add --renormalize .");

        return new Report(
            ChangedFiles: changedSet.ToArray(),
            UntrackedFiles: untrackedSet.ToArray(),
            SemanticFiles: semantic.ToArray(),
            WhitespaceOnlyFiles: whitespaceOnly.ToArray(),
            EolOnlyFiles: eolOnly.ToArray(),
            BomOnlyFiles: bomOnly.ToArray(),
            SuggestedSafeFixCommands: safeFixCommands);
    }

    public static IReadOnlyList<string> ParseNameOnlyOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var set = new SortedSet<string>(StringComparer.Ordinal);
        var lines = output.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var p = line.Trim();
            if (p.Length > 0)
                set.Add(p);
        }

        return set.ToArray();
    }

    private static SortedSet<string> ToOrderedSet(IEnumerable<string> values)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                set.Add(value.Trim());
        }
        return set;
    }

    internal static bool IsBomOnlyPatch(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
            return false;

        var removed = new List<string>();
        var added = new List<string>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');

        foreach (var line in lines)
        {
            if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("+++", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("-", StringComparison.Ordinal))
                removed.Add(line[1..]);
            else if (line.StartsWith("+", StringComparison.Ordinal))
                added.Add(line[1..]);
        }

        if (removed.Count == 0 || removed.Count != added.Count)
            return false;

        for (var i = 0; i < removed.Count; i++)
        {
            var oldLine = removed[i];
            var newLine = added[i];
            if (oldLine.Length == 0 || oldLine[0] != '\uFEFF')
                return false;
            if (!string.Equals(oldLine[1..], newLine, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
