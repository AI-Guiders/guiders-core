using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentFindings.Core;

/// <summary>
/// Workspace-local artifact-memo + task-DAG journal (workflow store, not KB).
/// Default: <c>{workspace}/.agent-findings/{memos|tasks}.jsonl</c>.
/// Override relative dir with env <c>AGENT_FINDINGS_DIR</c>.
/// </summary>
public static class WorkspaceFindingsStore
{
    public const string DefaultRelativeDir = ".agent-findings";
    public const string RelativeDirEnvVar = "AGENT_FINDINGS_DIR";
    public const string MemosFileName = "memos.jsonl";
    public const string TasksFileName = "tasks.jsonl";

    public static readonly string[] AllowedRelevance = ["on_task", "maybe", "off_task", "unknown"];
    public static readonly string[] AllowedDisposition = ["touch", "leave", "unsure"];
    public static readonly string[] AllowedMemoStatus = ["active", "stale", "superseded"];
    public static readonly string[] AllowedTaskStatus = ["pending", "ready", "in_progress", "done", "blocked"];

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions JsonIn = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string RelativeDirName()
    {
        var env = Environment.GetEnvironmentVariable(RelativeDirEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            var name = env.Trim().TrimStart('/', '\\');
            if (name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
                throw new InvalidOperationException(
                    $"{RelativeDirEnvVar} must be a single relative directory name, not a rooted path.");
            return name;
        }

        return DefaultRelativeDir;
    }

    public static string FindingsDir(string workspacePath) =>
        Path.Combine(Path.GetFullPath(workspacePath), RelativeDirName());

    public static string MemosFile(string workspacePath) =>
        Path.Combine(FindingsDir(workspacePath), MemosFileName);

    public static string TasksFile(string workspacePath) =>
        Path.Combine(FindingsDir(workspacePath), TasksFileName);

    /// <summary>Normalize to workspace-relative forward-slash path.</summary>
    public static string NormalizePath(string path)
    {
        var p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal))
            p = p[2..];
        p = p.TrimStart('/');
        if (p.Length == 0)
            throw new ArgumentException("path is required.");
        if (p.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("path must not contain '..'.");
        return p;
    }

    public static string HashFile(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string? TryHashWorkspaceFile(string workspacePath, string relativePath)
    {
        var full = Path.Combine(Path.GetFullPath(workspacePath), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
            return null;
        return HashFile(full);
    }

    public static FindingRecord UpsertMemo(
        string workspacePath,
        string path,
        string? contentHash,
        string? relevance,
        string? disposition,
        string? summary,
        string? anchors,
        IReadOnlyList<string>? dependsOnPaths,
        IReadOnlyList<string>? taskIds,
        string? status,
        string? sessionId)
    {
        var rel = NormalizePath(path);
        var hash = string.IsNullOrWhiteSpace(contentHash)
            ? TryHashWorkspaceFile(workspacePath, rel)
            : contentHash.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException(
                "content_hash is required when the file is missing on disk (pass content_hash= or ensure path exists).");

        var st = NormalizeEnum(status, "active", AllowedMemoStatus, "status");
        var relv = NormalizeOptionalEnum(relevance, AllowedRelevance, "relevance");
        var disp = NormalizeOptionalEnum(disposition, AllowedDisposition, "disposition");

        Directory.CreateDirectory(FindingsDir(workspacePath));

        var deps = NormalizePathList(dependsOnPaths);
        Dictionary<string, string>? depHashes = null;
        if (deps is not null)
        {
            depHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in deps)
            {
                var dh = TryHashWorkspaceFile(workspacePath, dep);
                if (dh is not null)
                    depHashes[dep] = dh;
            }

            if (depHashes.Count == 0)
                depHashes = null;
        }

        var record = new FindingRecord(
            Id: NewId(),
            AtUtc: DateTimeOffset.UtcNow,
            Path: rel,
            ContentHash: hash!,
            Relevance: relv,
            Disposition: disp,
            Summary: TrimOrNull(summary),
            Anchors: TrimOrNull(anchors),
            DependsOnPaths: deps,
            DependsOnHashes: depHashes,
            TaskIds: NormalizeTokenList(taskIds),
            Status: st,
            SessionId: TrimOrNull(sessionId));

        AppendJsonl(MemosFile(workspacePath), record);
        return record;
    }

    /// <summary>Backward-compatible alias for <see cref="UpsertMemo"/>.</summary>
    public static FindingRecord Upsert(
        string workspacePath,
        string path,
        string? contentHash,
        string? relevance,
        string? disposition,
        string? summary,
        string? anchors,
        IReadOnlyList<string>? dependsOnPaths,
        IReadOnlyList<string>? taskIds,
        string? status,
        string? sessionId) =>
        UpsertMemo(workspacePath, path, contentHash, relevance, disposition, summary, anchors,
            dependsOnPaths, taskIds, status, sessionId);

    public static IReadOnlyList<FindingRecord> ListMemos(
        string workspacePath,
        string? path,
        string? taskId,
        string? status,
        bool latestOnly,
        int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        var records = ReadJsonl<FindingRecord>(MemosFile(workspacePath)).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(path))
        {
            var want = NormalizePath(path);
            records = records.Where(r =>
                string.Equals(r.Path, want, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(taskId))
        {
            var tid = taskId.Trim();
            records = records.Where(r =>
                r.TaskIds is not null &&
                r.TaskIds.Any(t => string.Equals(t, tid, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim();
            records = records.Where(r =>
                string.Equals(r.Status, st, StringComparison.OrdinalIgnoreCase));
        }

        if (latestOnly)
        {
            records = records
                .GroupBy(r => MemoScopeKey(r), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.AtUtc).First());
        }

        return records
            .OrderByDescending(r => r.AtUtc)
            .Take(limit)
            .ToList();
    }

    /// <summary>Backward-compatible alias for <see cref="ListMemos"/>.</summary>
    public static IReadOnlyList<FindingRecord> List(
        string workspacePath,
        string? path,
        string? taskId,
        string? status,
        bool latestOnly,
        int limit) =>
        ListMemos(workspacePath, path, taskId, status, latestOnly, limit);

    public static FindingFreshness Check(string workspacePath, string path, string? taskId)
    {
        var rel = NormalizePath(path);
        var latest = ListMemos(workspacePath, rel, taskId, status: null, latestOnly: true, limit: 1)
            .FirstOrDefault();
        var current = TryHashWorkspaceFile(workspacePath, rel);
        if (latest is null)
        {
            return new FindingFreshness(rel, current, Memo: null, HashMatch: null, DepsOk: null,
                StaleDeps: null, Advice: "no_memo");
        }

        if (current is null)
        {
            return new FindingFreshness(rel, CurrentHash: null, latest, HashMatch: null, DepsOk: null,
                StaleDeps: null, Advice: "file_missing");
        }

        var match = string.Equals(latest.ContentHash, current, StringComparison.OrdinalIgnoreCase);
        if (!match)
        {
            return new FindingFreshness(rel, current, latest, HashMatch: false, DepsOk: null,
                StaleDeps: null, Advice: "reread_file");
        }

        var staleDeps = FindStaleDeps(workspacePath, latest);
        if (staleDeps.Count > 0)
        {
            return new FindingFreshness(rel, current, latest, HashMatch: true, DepsOk: false,
                StaleDeps: staleDeps, Advice: "stale_deps");
        }

        return new FindingFreshness(rel, current, latest, HashMatch: true, DepsOk: true,
            StaleDeps: null, Advice: "reuse_memo");
    }

    public static FindingRecord? GetMemo(string workspacePath, string id)
    {
        var want = id.Trim();
        return ReadJsonl<FindingRecord>(MemosFile(workspacePath))
            .FirstOrDefault(r => string.Equals(r.Id, want, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Backward-compatible alias for <see cref="GetMemo"/>.</summary>
    public static FindingRecord? Get(string workspacePath, string id) => GetMemo(workspacePath, id);

    public static TaskRecord UpsertTask(
        string workspacePath,
        string taskId,
        string? title,
        string? asIs,
        string? toBe,
        string? why,
        IReadOnlyList<string>? blockedBy,
        IReadOnlyList<string>? unlocks,
        IReadOnlyList<string>? memberPaths,
        string? status,
        string? sessionId)
    {
        var tid = RequireToken(taskId, "task_id");
        Directory.CreateDirectory(FindingsDir(workspacePath));
        var path = TasksFile(workspacePath);

        return JsonlExclusiveAppend.WithExclusive(path, () =>
        {
            var prior = GetTask(workspacePath, tid)?.Task;

            // Null fields keep prior revision (partial update). Explicit empty lists clear.
            var mergedTitle = title ?? prior?.Title;
            var mergedAsIs = asIs ?? prior?.AsIs;
            var mergedToBe = toBe ?? prior?.ToBe;
            var mergedWhy = why ?? prior?.Why;
            var mergedBlockedBy = blockedBy ?? prior?.BlockedBy;
            var mergedUnlocks = unlocks ?? prior?.Unlocks;
            var mergedMemberPaths = memberPaths ?? prior?.MemberPaths;
            var mergedSessionId = sessionId ?? prior?.SessionId;
            var st = NormalizeEnum(
                string.IsNullOrWhiteSpace(status) ? prior?.Status : status,
                "pending",
                AllowedTaskStatus,
                "status");

            var record = new TaskRecord(
                Id: NewId(),
                AtUtc: DateTimeOffset.UtcNow,
                TaskId: tid,
                Title: TrimOrNull(mergedTitle),
                AsIs: TrimOrNull(mergedAsIs),
                ToBe: TrimOrNull(mergedToBe),
                Why: TrimOrNull(mergedWhy),
                BlockedBy: NormalizeTokenList(mergedBlockedBy),
                Unlocks: NormalizeTokenList(mergedUnlocks),
                MemberPaths: NormalizePathList(mergedMemberPaths),
                Status: st,
                SessionId: TrimOrNull(mergedSessionId));

            AppendJsonlUnlocked(path, record);
            return record;
        });
    }

    public static IReadOnlyList<TaskView> ListTasks(
        string workspacePath,
        string? taskId,
        string? status,
        bool latestOnly,
        int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        var records = ReadJsonl<TaskRecord>(TasksFile(workspacePath)).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(taskId))
        {
            var tid = taskId.Trim();
            records = records.Where(r =>
                string.Equals(r.TaskId, tid, StringComparison.OrdinalIgnoreCase));
        }

        if (latestOnly)
        {
            records = records
                .GroupBy(r => r.TaskId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.AtUtc).First());
        }

        var latestById = ReadJsonl<TaskRecord>(TasksFile(workspacePath))
            .GroupBy(r => r.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AtUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        var views = records
            .Select(r => ToTaskView(r, latestById))
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim();
            views = views.Where(v =>
                string.Equals(v.EffectiveStatus, st, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v.Task.Status, st, StringComparison.OrdinalIgnoreCase));
        }

        return views
            .OrderByDescending(v => v.Task.AtUtc)
            .Take(limit)
            .ToList();
    }

    public static TaskView? GetTask(string workspacePath, string taskId)
    {
        return ListTasks(workspacePath, taskId, status: null, latestOnly: true, limit: 1)
            .FirstOrDefault();
    }

    private static TaskView ToTaskView(TaskRecord task, IReadOnlyDictionary<string, TaskRecord> latestById)
    {
        if (string.Equals(task.Status, "done", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(task.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
        {
            return new TaskView(task, task.Status, WaitingOn: null);
        }

        var waiting = new List<string>();
        if (task.BlockedBy is { Count: > 0 })
        {
            foreach (var blocker in task.BlockedBy)
            {
                if (!latestById.TryGetValue(blocker, out var b) ||
                    !string.Equals(b.Status, "done", StringComparison.OrdinalIgnoreCase))
                {
                    waiting.Add(blocker);
                }
            }
        }

        if (waiting.Count > 0)
            return new TaskView(task, "blocked", waiting);

        if (string.Equals(task.Status, "blocked", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(task.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return new TaskView(task, "ready", WaitingOn: null);
        }

        return new TaskView(task, task.Status, WaitingOn: null);
    }

    private static List<string> FindStaleDeps(string workspacePath, FindingRecord memo)
    {
        var stale = new List<string>();
        if (memo.DependsOnHashes is { Count: > 0 })
        {
            foreach (var (depPath, recordedHash) in memo.DependsOnHashes)
            {
                var now = TryHashWorkspaceFile(workspacePath, depPath);
                if (now is null ||
                    !string.Equals(now, recordedHash, StringComparison.OrdinalIgnoreCase))
                {
                    stale.Add(depPath);
                }
            }
        }
        else if (memo.DependsOnPaths is { Count: > 0 })
        {
            // Legacy memos without snapshotted hashes: cannot prove freshness of deps.
            foreach (var dep in memo.DependsOnPaths)
            {
                if (TryHashWorkspaceFile(workspacePath, dep) is null)
                    stale.Add(dep);
            }
        }

        return stale;
    }

    private static string MemoScopeKey(FindingRecord r)
    {
        var tasks = r.TaskIds is { Count: > 0 }
            ? string.Join(",", r.TaskIds.Order(StringComparer.OrdinalIgnoreCase))
            : "";
        return r.Path + "|" + tasks;
    }

    private static void AppendJsonl<T>(string path, T record)
    {
        var line = JsonSerializer.Serialize(record, JsonOut);
        JsonlExclusiveAppend.AppendLine(path, line + "\n");
    }

    private static void AppendJsonlUnlocked<T>(string path, T record)
    {
        var line = JsonSerializer.Serialize(record, JsonOut);
        JsonlExclusiveAppend.AppendLineUnlocked(path, line + "\n");
    }

    private static List<T> ReadJsonl<T>(string path)
    {
        var list = new List<T>();
        foreach (var line in JsonlExclusiveAppend.ReadAllLinesShared(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var rec = JsonSerializer.Deserialize<T>(line, JsonIn);
                if (rec is not null)
                    list.Add(rec);
            }
            catch (JsonException)
            {
                // skip corrupt line
            }
        }

        return list;
    }

    private static IReadOnlyList<string>? NormalizePathList(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0)
            return null;
        var list = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? null : list;
    }

    private static IReadOnlyList<string>? NormalizeTokenList(IReadOnlyList<string>? tokens)
    {
        if (tokens is null || tokens.Count == 0)
            return null;
        var list = tokens
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? null : list;
    }

    private static string NormalizeEnum(string? value, string defaultValue, string[] allowed, string name)
    {
        var v = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToLowerInvariant();
        if (!allowed.Contains(v, StringComparer.Ordinal))
            throw new ArgumentException($"{name} must be one of: {string.Join(", ", allowed)}.");
        return v;
    }

    private static string? NormalizeOptionalEnum(string? value, string[] allowed, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.Trim().ToLowerInvariant();
        if (!allowed.Contains(v, StringComparer.Ordinal))
            throw new ArgumentException($"{name} must be one of: {string.Join(", ", allowed)}.");
        return v;
    }

    private static string RequireToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.");
        return value.Trim();
    }

    private static string? TrimOrNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
