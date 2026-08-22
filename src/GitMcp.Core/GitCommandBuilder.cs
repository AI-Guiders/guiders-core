using System.Globalization;

namespace GitMcp.Core;

/// <summary>Единая сборка аргументов для вызова <c>git</c> (паритет GitMcp и Cascade IDE).</summary>
public static class GitCommandBuilder
{
    public const int LogCountDefault = 20;
    public const int LogCountMax = 500;

    /// <summary>Совпадает с вкладкой Git / телеметрией IDE: <c>git status --short --branch</c>.</summary>
    public static IReadOnlyList<string> StatusShortBranch() => ["status", "--short", "--branch"];

    /// <summary>Две команды подряд, как в MCP git_status: ветка и полный <c>status</c>.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> StatusMcpSequence() =>
    [
        ["rev-parse", "--abbrev-ref", "HEAD"],
        ["status"]
    ];

    public static IReadOnlyList<string> Diff(bool staged, string? path)
    {
        var list = new List<string> { "diff" };
        if (staged)
            list.Add("--staged");
        if (!string.IsNullOrWhiteSpace(path))
        {
            list.Add("--");
            list.Add(path.Trim());
        }
        return list;
    }

    /// <summary>Per-file add/del counts for diff scene list mode.</summary>
    public static IReadOnlyList<string> DiffNumstat(bool staged, string? path = null)
    {
        var list = new List<string> { "diff", "--numstat" };
        if (staged)
            list.Add("--staged");
        if (!string.IsNullOrWhiteSpace(path))
        {
            list.Add("--");
            list.Add(path.Trim());
        }
        return list;
    }

    /// <summary>
    /// Список изменённых путей (name-only) для preflight-классификации шума.
    /// </summary>
    public static IReadOnlyList<string> DiffNameOnly(bool staged, bool ignoreWhitespace = false, bool ignoreCrAtEol = false)
    {
        var list = new List<string> { "diff", "--name-only" };
        if (staged)
            list.Add("--staged");
        if (ignoreWhitespace)
            list.Add("-w");
        if (ignoreCrAtEol)
            list.Add("--ignore-cr-at-eol");
        return list;
    }

    /// <summary>
    /// Патч одного файла (для эвристик BOM-only и диагностики preflight).
    /// </summary>
    public static GitArgsResult DiffPatchForPath(bool staged, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return GitArgsResult.Fail("git_preflight: file path is required for DiffPatchForPath.");
        var list = new List<string> { "diff" };
        if (staged)
            list.Add("--staged");
        list.Add("--");
        list.Add(path.Trim());
        return GitArgsResult.Ok(list);
    }

    /// <summary>
    /// Безопасная нормализация line endings по .gitattributes.
    /// </summary>
    public static IReadOnlyList<string> AddRenormalize() => ["add", "--renormalize", "."];

    /// <summary>
    /// Показать только untracked пути (без ignored) для preflight.
    /// </summary>
    public static IReadOnlyList<string> ListUntracked() => ["ls-files", "--others", "--exclude-standard"];

    public static int ClampLogCount(int n)
    {
        if (n <= 0)
            return LogCountDefault;
        return n > LogCountMax ? LogCountMax : n;
    }

    public static IReadOnlyList<string> Log(int count) =>
        ["log", "-n", ClampLogCount(count).ToString(CultureInfo.InvariantCulture), "--oneline"];

    public static IReadOnlyList<string> Add(IReadOnlyList<string>? paths)
    {
        var list = new List<string> { "add" };
        if (paths is { Count: > 0 })
        {
            var nonEmpty = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            if (nonEmpty.Count > 0)
            {
                list.Add("--");
                list.AddRange(nonEmpty);
                return list;
            }
        }
        list.Add("-A");
        return list;
    }

    public static IReadOnlyList<string> Commit(string message) => ["commit", "-m", message];

    /// <param name="defaultOriginWhenRemoteEmpty">Если true и remote пуст — подставить <c>origin</c> (поведение MCP git_push).</param>
    /// <param name="dryRun">Если true — <c>git push --dry-run</c> (без отправки объектов).</param>
    public static IReadOnlyList<string> Push(string? remote, string? branch, bool defaultOriginWhenRemoteEmpty, bool dryRun = false)
    {
        var list = new List<string> { "push" };
        if (dryRun)
            list.Add("--dry-run");
        string? r = string.IsNullOrWhiteSpace(remote) ? null : remote.Trim();
        if (r is null && defaultOriginWhenRemoteEmpty)
            r = "origin";
        if (r is not null)
            list.Add(r);
        if (!string.IsNullOrWhiteSpace(branch))
            list.Add(branch.Trim());
        return list;
    }

    /// <param name="dryRun">Если true — <c>git fetch --dry-run</c> (что бы сделал fetch без обновления refs).</param>
    public static GitArgsResult Fetch(bool all, bool prune, string? remote, bool dryRun = false)
    {
        if (all && !string.IsNullOrWhiteSpace(remote))
            return GitArgsResult.Fail("git_fetch: do not pass remote when all=true.");
        var list = new List<string> { "fetch" };
        if (dryRun)
            list.Add("--dry-run");
        if (all)
        {
            list.Add("--all");
            if (prune)
                list.Add("--prune");
        }
        else
        {
            if (prune)
                list.Add("--prune");
            if (!string.IsNullOrWhiteSpace(remote))
                list.Add(remote.Trim());
        }
        return GitArgsResult.Ok(list);
    }

    /// <param name="dryRun">Если true — <c>git pull --dry-run</c> (без изменения рабочей копии; требуется Git 2.27+).</param>
    public static GitArgsResult Pull(string? remote, string? branch, bool ffOnly, bool dryRun = false)
    {
        var pullRem = remote?.Trim() ?? "";
        var pullBr = branch?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(pullRem) != string.IsNullOrWhiteSpace(pullBr))
            return GitArgsResult.Fail("git_pull: specify both remote and branch, or neither (pull upstream).");
        var list = new List<string> { "pull" };
        if (dryRun)
            list.Add("--dry-run");
        if (ffOnly)
            list.Add("--ff-only");
        if (!string.IsNullOrWhiteSpace(pullRem))
        {
            list.Add(pullRem);
            list.Add(pullBr);
        }
        return GitArgsResult.Ok(list);
    }

    public static GitArgsResult BranchList() => GitArgsResult.Ok(["branch", "-vv"]);

    public static GitArgsResult BranchCreate(string name, string? startPoint)
    {
        if (string.IsNullOrWhiteSpace(name))
            return GitArgsResult.Fail("git_branch create: name is required.");
        var list = new List<string> { "branch", name.Trim() };
        if (!string.IsNullOrWhiteSpace(startPoint))
            list.Add(startPoint.Trim());
        return GitArgsResult.Ok(list);
    }

    public static GitArgsResult BranchDelete(string name, bool force)
    {
        if (string.IsNullOrWhiteSpace(name))
            return GitArgsResult.Fail("git_branch delete: name is required.");
        return GitArgsResult.Ok(["branch", force ? "-D" : "-d", name.Trim()]);
    }

    public static GitArgsResult Show(string rev, string? path, bool statOnly)
    {
        if (string.IsNullOrWhiteSpace(rev))
            return GitArgsResult.Fail("git_show: rev is required.");
        var r = rev.Trim();
        if (statOnly)
            return GitArgsResult.Ok(["show", "--stat", r]);
        if (!string.IsNullOrWhiteSpace(path))
            return GitArgsResult.Ok(["show", r, "--", path.Trim()]);
        return GitArgsResult.Ok(["show", r]);
    }

    public static GitArgsResult SubmoduleStatus() => GitArgsResult.Ok(["submodule", "status"]);

    /// <summary>Porcelain for SCM scene counts (paths not returned to agent by default).</summary>
    public static IReadOnlyList<string> StatusPorcelain() => ["status", "--porcelain=v1"];

    public static IReadOnlyList<string> RevParseAbbrevHead() => ["rev-parse", "--abbrev-ref", "HEAD"];

    public static IReadOnlyList<string> RevParseAbbrevUpstream() => ["rev-parse", "--abbrev-ref", "@{upstream}"];

    /// <summary>Ahead (left) / behind (right) vs upstream.</summary>
    public static IReadOnlyList<string> AheadBehindUpstream() =>
        ["rev-list", "--left-right", "--count", "HEAD...@{upstream}"];

    public static GitArgsResult SubmoduleUpdate(bool recursive, string? path)
    {
        var list = new List<string> { "submodule", "update", "--init" };
        if (recursive)
            list.Add("--recursive");
        if (!string.IsNullOrWhiteSpace(path))
        {
            list.Add("--");
            list.Add(path.Trim());
        }
        return GitArgsResult.Ok(list);
    }
}
