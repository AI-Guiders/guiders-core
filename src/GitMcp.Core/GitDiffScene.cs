namespace GitMcp.Core;

/// <summary>Compact diff scene — file list first, hunks per path (agent comfort / ADR 0178).</summary>
public static class GitDiffScene
{
    public const string SchemaVersion = "git_diff_scene/v0";
    public const int MaxFilesDefault = 80;
    public const int MaxHunksDefault = 40;
    public const int MaxHunkLinesDefault = 200;

    public sealed record FileRow(string Path, string Area, int Additions, int Deletions);

    public sealed record HunkLine(string Op, string Text);

    public sealed record Hunk(string Header, IReadOnlyList<HunkLine> Lines);

    /// <summary>Parse <c>git diff --numstat</c> lines: additions TAB deletions TAB path (or path1 =&gt; path2).</summary>
    public static IReadOnlyList<FileRow> ParseNumstat(string output, string area, int maxFiles)
    {
        var list = new List<FileRow>();
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (list.Count >= maxFiles)
                break;
            var parts = raw.Split('\t');
            if (parts.Length < 3)
                continue;
            var addStr = parts[0].Trim();
            var delStr = parts[1].Trim();
            var path = parts[^1].Trim();
            // rename: "old => new" in last field sometimes; prefer last segment after =>
            var arrow = path.LastIndexOf(" => ", StringComparison.Ordinal);
            if (arrow >= 0)
                path = path[(arrow + 4)..].Trim();
            var additions = addStr == "-" ? 0 : (int.TryParse(addStr, out var a) ? a : 0);
            var deletions = delStr == "-" ? 0 : (int.TryParse(delStr, out var d) ? d : 0);
            if (string.IsNullOrWhiteSpace(path))
                continue;
            list.Add(new FileRow(path, area, additions, deletions));
        }

        return list;
    }

    public static IReadOnlyList<FileRow> ParseUntracked(string lsOthers, int maxFiles)
    {
        var list = new List<FileRow>();
        foreach (var raw in lsOthers.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (list.Count >= maxFiles)
                break;
            var path = raw.Trim();
            if (path.Length == 0)
                continue;
            list.Add(new FileRow(path, "untracked", 0, 0));
        }

        return list;
    }

    /// <summary>Parse unified diff into hunks; caps hunk count and lines per hunk.</summary>
    public static (IReadOnlyList<Hunk> Hunks, bool Truncated) ParseUnifiedDiff(
        string diff, int maxHunks, int maxLinesPerHunk)
    {
        var hunks = new List<Hunk>();
        string? header = null;
        var lines = new List<HunkLine>();
        var truncated = false;

        void Flush()
        {
            if (header is null)
                return;
            if (hunks.Count >= maxHunks)
            {
                truncated = true;
                header = null;
                lines.Clear();
                return;
            }

            hunks.Add(new Hunk(header, lines.ToList()));
            header = null;
            lines = [];
        }

        foreach (var raw in diff.Split(['\r', '\n'], StringSplitOptions.None))
        {
            if (raw.StartsWith("diff --git ", StringComparison.Ordinal)
                || raw.StartsWith("index ", StringComparison.Ordinal)
                || raw.StartsWith("--- ", StringComparison.Ordinal)
                || raw.StartsWith("+++ ", StringComparison.Ordinal)
                || raw.StartsWith("new file mode", StringComparison.Ordinal)
                || raw.StartsWith("deleted file mode", StringComparison.Ordinal)
                || raw.StartsWith("similarity index", StringComparison.Ordinal)
                || raw.StartsWith("rename from", StringComparison.Ordinal)
                || raw.StartsWith("rename to", StringComparison.Ordinal))
                continue;

            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                Flush();
                if (truncated)
                    break;
                header = raw;
                continue;
            }

            if (header is null)
                continue;

            if (lines.Count >= maxLinesPerHunk)
            {
                truncated = true;
                continue;
            }

            if (raw.Length == 0)
            {
                lines.Add(new HunkLine(" ", ""));
                continue;
            }

            var op = raw[0] switch
            {
                '+' => "+",
                '-' => "-",
                '\\' => "\\",
                _ => " "
            };
            var text = raw.Length > 1 ? raw[1..] : "";
            if (op == " " && raw[0] is not ' ' and not '\t')
            {
                // context without leading space (rare) — keep whole line as context
                lines.Add(new HunkLine(" ", raw));
            }
            else
                lines.Add(new HunkLine(op, text));
        }

        Flush();
        return (hunks, truncated);
    }
}
