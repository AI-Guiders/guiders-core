namespace GitMcp.Core;

/// <summary>Compact SCM scene (ADR 0178) — counts + submodule map, not full porcelain dump.</summary>
public static class GitScene
{
    public const string SchemaVersion = "git_scene/v0";
    public const int MaxRootsDefault = 16;
    public const int MaxSubmodulesDefault = 64;

    public sealed record DirtyCounts(int Staged, int Unstaged, int Untracked)
    {
        public bool IsDirty => Staged > 0 || Unstaged > 0 || Untracked > 0;
    }

    public sealed record SubmoduleEntry(string Path, string Sha, string Flag, string? BranchHint);

    public static DirtyCounts CountPorcelain(string porcelain)
    {
        var staged = 0;
        var unstaged = 0;
        var untracked = 0;
        foreach (var raw in porcelain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw;
            if (line.Length < 2)
                continue;
            // rename: "R  old -> new" still starts with XY
            var x = line[0];
            var y = line[1];
            if (x == '?' && y == '?')
            {
                untracked++;
                continue;
            }

            if (x is not ' ' and not '?')
                staged++;
            if (y is not ' ' and not '?')
                unstaged++;
        }

        return new DirtyCounts(staged, unstaged, untracked);
    }

    /// <summary>Parse <c>git rev-list --left-right --count A...B</c> → ahead (left), behind (right).</summary>
    public static (int Ahead, int Behind)? ParseLeftRightCount(string output)
    {
        var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(line))
            return null;
        var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;
        if (!int.TryParse(parts[0], out var ahead) || !int.TryParse(parts[1], out var behind))
            return null;
        return (ahead, behind);
    }

    /// <summary>Parse lines from <c>git submodule status</c>.</summary>
    public static IReadOnlyList<SubmoduleEntry> ParseSubmoduleStatus(string output, int maxEntries)
    {
        var list = new List<SubmoduleEntry>();
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (list.Count >= maxEntries)
                break;
            var line = raw;
            if (line.Length < 2)
                continue;
            var flag = line[0] is ' ' or '-' or '+' or 'U' ? line[0].ToString() : " ";
            var rest = line[0] is ' ' or '-' or '+' or 'U' ? line[1..].TrimStart() : line.TrimStart();
            var tokens = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;
            var sha = tokens[0];
            var pathAndHint = tokens[1].Trim();
            string path = pathAndHint;
            string? hint = null;
            var paren = pathAndHint.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && pathAndHint.EndsWith(')'))
            {
                path = pathAndHint[..paren].Trim();
                hint = pathAndHint[(paren + 2)..^1];
            }

            list.Add(new SubmoduleEntry(path, sha, flag, hint));
        }

        return list;
    }
}
