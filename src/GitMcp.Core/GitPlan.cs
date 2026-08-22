namespace GitMcp.Core;

/// <summary>Logical multi-root commit plan — draft from porcelain, validate/apply via slices.</summary>
public static class GitPlan
{
    public const string SchemaVersion = "git_plan/v0";
    public const int MaxPathsPerRootDefault = 200;

    /// <summary>Parse <c>git status --porcelain=v1</c> into unique paths (renames → new path).</summary>
    public static IReadOnlyList<string> ParsePorcelainPaths(string porcelain, int maxPaths)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in porcelain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (list.Count >= maxPaths)
                break;
            if (raw.Length < 4)
                continue;

            // XY[ SP...]path  OR  XY[ SP...]old -> new
            var body = raw[2..].TrimStart();
            if (body.Length == 0)
                continue;

            var arrow = body.IndexOf(" -> ", StringComparison.Ordinal);
            var path = arrow >= 0 ? body[(arrow + 4)..].Trim() : body.Trim();
            // quoted paths: "foo bar"
            if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
                path = path[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);

            if (path.Length == 0 || !seen.Add(path))
                continue;
            list.Add(path);
        }

        return list;
    }
}
