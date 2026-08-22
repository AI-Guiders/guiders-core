using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgentFailures.Core;

/// <summary>
/// Workspace-local failure journal (workflow store, not KB).
/// Default path: <c>{workspace}/.agent-failures/{tool}.jsonl</c> — host-neutral (Cursor, CIDE, CLI).
/// Override relative dir with env <c>AGENT_FAILURES_DIR</c>.
/// </summary>
public static partial class WorkspaceFailuresStore
{
    public const string DefaultRelativeDir = ".agent-failures";
    public const string RelativeDirEnvVar = "AGENT_FAILURES_DIR";

    /// <summary>Same fingerprint within this window without new resolution → no new line.</summary>
    public static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(15);

    public static readonly string[] AllowedCategories =
    [
        "incorrect_invocation",
        "missing_precondition",
        "environment",
        "tool_bug",
        "unknown",
    ];

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

    public static string FailuresDir(string workspacePath) =>
        Path.Combine(Path.GetFullPath(workspacePath), RelativeDirName());

    public static string FileForTool(string workspacePath, string tool) =>
        Path.Combine(FailuresDir(workspacePath), SafeToolFileName(tool) + ".jsonl");

    public static string SafeToolFileName(string tool)
    {
        var t = tool.Trim();
        if (t.Length == 0)
            throw new ArgumentException("tool is required.");
        var safe = SafeToolChars().Replace(t, "_");
        return safe.Length == 0 ? "unknown" : safe;
    }

    public static string Fingerprint(string tool, string? errorOrMiss)
    {
        var shape = NormalizeErrorShape(errorOrMiss);
        var raw = tool.Trim() + "|" + shape;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static string NormalizeErrorShape(string? errorOrMiss)
    {
        if (string.IsNullOrWhiteSpace(errorOrMiss))
            return "empty";
        var line = errorOrMiss.Replace('\r', '\n').Split('\n', 2, StringSplitOptions.None)[0].Trim();
        if (line.Length > 160)
            line = line[..160];
        line = Regex.Replace(line, @"[A-Za-z]:\\[^\s]+", "<path>");
        line = Regex.Replace(line, @"/[^\s]+", m => m.Value.Contains('.') ? "<path>" : m.Value);
        return line;
    }

    public static string NormalizeCategory(string? category)
    {
        var c = string.IsNullOrWhiteSpace(category) ? "unknown" : category.Trim().ToLowerInvariant();
        if (!AllowedCategories.Contains(c, StringComparer.Ordinal))
            throw new ArgumentException($"category must be one of: {string.Join(", ", AllowedCategories)}.");
        return c;
    }

    /// <summary>
    /// Record a miss and/or resolution. Same fingerprint within <see cref="DedupeWindow"/>
    /// without new resolution fields → no append (deduped). Resolution fields merge onto prior.
    /// </summary>
    public static FailureView Record(
        string workspacePath,
        string tool,
        string? errorOrMiss,
        string? argsTried,
        string? resolution,
        string? correctArgs,
        string? why,
        string? fingerprint,
        string? taskId,
        string? category,
        string? projectId,
        string? app,
        string? suggestedNext)
    {
        Directory.CreateDirectory(FailuresDir(workspacePath));
        var toolName = tool.Trim();
        var fp = string.IsNullOrWhiteSpace(fingerprint)
            ? Fingerprint(toolName, errorOrMiss)
            : fingerprint.Trim();

        var path = FileForTool(workspacePath, toolName);
        return JsonlExclusiveAppend.WithExclusive(path, () =>
        {
            var priorSame = ReadFile(path)
                .Where(r => string.Equals(r.Fingerprint, fp, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.AtUtc)
                .ToList();
            var last = priorSame.LastOrDefault();

            var hasResolutionPatch =
                !string.IsNullOrWhiteSpace(resolution) ||
                !string.IsNullOrWhiteSpace(correctArgs) ||
                !string.IsNullOrWhiteSpace(why) ||
                !string.IsNullOrWhiteSpace(suggestedNext);

            return RecordCore(
                path, toolName, errorOrMiss, argsTried, resolution, correctArgs, why,
                fp, taskId, category, projectId, app, suggestedNext, priorSame, last, hasResolutionPatch);
        });
    }

    private static FailureView RecordCore(
        string path,
        string toolName,
        string? errorOrMiss,
        string? argsTried,
        string? resolution,
        string? correctArgs,
        string? why,
        string fp,
        string? taskId,
        string? category,
        string? projectId,
        string? app,
        string? suggestedNext,
        List<FailureRecord> priorSame,
        FailureRecord? last,
        bool hasResolutionPatch)
    {
        var now = DateTimeOffset.UtcNow;

        if (last is not null &&
            !hasResolutionPatch &&
            now - last.AtUtc <= DedupeWindow &&
            NoMetaPatch(category, projectId, app, taskId, argsTried))
        {
            return new FailureView(last, ResolveSuggestedNext(last), Deduped: true);
        }

        int seen;
        string? seenBefore;
        if (last is not null && hasResolutionPatch)
        {
            // Resolution upsert: merge onto prior, do not inflate seenCount.
            seen = last.SeenCount;
            seenBefore = last.Id;
            errorOrMiss = FirstNonEmpty(errorOrMiss, last.ErrorOrMiss);
            argsTried = FirstNonEmpty(argsTried, last.ArgsTried);
            resolution = FirstNonEmpty(resolution, last.Resolution);
            correctArgs = FirstNonEmpty(correctArgs, last.CorrectArgs);
            why = FirstNonEmpty(why, last.Why);
            suggestedNext = FirstNonEmpty(suggestedNext, last.SuggestedNext);
            category = FirstNonEmpty(category, last.Category);
            projectId = FirstNonEmpty(projectId, last.ProjectId);
            app = FirstNonEmpty(app, last.App);
            taskId = FirstNonEmpty(taskId, last.TaskId);
        }
        else
        {
            seen = priorSame.Count + 1;
            seenBefore = last?.Id;
            if (last is not null)
            {
                // Carry meta forward when omitted on a fresh sighting.
                category = FirstNonEmpty(category, last.Category);
                projectId = FirstNonEmpty(projectId, last.ProjectId);
                app = FirstNonEmpty(app, last.App);
            }
        }

        var cat = NormalizeCategory(category);
        var record = new FailureRecord(
            Id: Guid.NewGuid().ToString("N")[..12],
            AtUtc: now,
            Tool: toolName,
            Fingerprint: fp,
            ErrorOrMiss: TrimOrNull(errorOrMiss),
            ArgsTried: RedactArgs(argsTried),
            Resolution: TrimOrNull(resolution),
            CorrectArgs: RedactArgs(correctArgs),
            Why: TrimOrNull(why),
            SeenCount: seen,
            SeenBefore: seenBefore,
            TaskId: TrimOrNull(taskId),
            Category: cat,
            ProjectId: TrimOrNull(projectId),
            App: TrimOrNull(app),
            SuggestedNext: TrimOrNull(suggestedNext));

        JsonlExclusiveAppend.AppendLineUnlocked(path, JsonSerializer.Serialize(record, JsonOut) + "\n");
        return new FailureView(record, ResolveSuggestedNext(record), Deduped: false);
    }

    /// <summary>Backward-compatible append (category defaults to unknown).</summary>
    public static FailureRecord Append(
        string workspacePath,
        string tool,
        string? errorOrMiss,
        string? argsTried,
        string? resolution,
        string? correctArgs,
        string? why,
        string? fingerprint,
        string? taskId) =>
        Record(workspacePath, tool, errorOrMiss, argsTried, resolution, correctArgs, why,
            fingerprint, taskId, category: null, projectId: null, app: null, suggestedNext: null).Record;

    public static IReadOnlyList<FailureView> List(
        string workspacePath,
        string? tool,
        string? fingerprint,
        string? category,
        string? projectId,
        string? app,
        string? taskId,
        bool latestOnly,
        int limit)
    {
        limit = Math.Clamp(limit, 1, 100);
        IEnumerable<FailureRecord> records;
        if (!string.IsNullOrWhiteSpace(tool))
        {
            records = ReadTool(workspacePath, tool);
        }
        else
        {
            var dir = FailuresDir(workspacePath);
            if (!Directory.Exists(dir))
                return [];
            records = Directory.EnumerateFiles(dir, "*.jsonl")
                .SelectMany(ReadFile);
        }

        if (!string.IsNullOrWhiteSpace(fingerprint))
            records = records.Where(r =>
                string.Equals(r.Fingerprint, fingerprint.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = NormalizeCategory(category);
            records = records.Where(r =>
                string.Equals(r.Category ?? "unknown", cat, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var pid = projectId.Trim();
            records = records.Where(r =>
                string.Equals(r.ProjectId, pid, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(app))
        {
            var a = app.Trim();
            records = records.Where(r =>
                string.Equals(r.App, a, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(taskId))
        {
            var tid = taskId.Trim();
            records = records.Where(r =>
                string.Equals(r.TaskId, tid, StringComparison.OrdinalIgnoreCase));
        }

        if (latestOnly)
        {
            records = records
                .GroupBy(r => r.Tool + "|" + r.Fingerprint, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.AtUtc).First());
        }

        return records
            .OrderByDescending(r => r.AtUtc)
            .Take(limit)
            .Select(r => new FailureView(r, ResolveSuggestedNext(r)))
            .ToList();
    }

    /// <summary>Legacy list overload.</summary>
    public static IReadOnlyList<FailureRecord> List(
        string workspacePath,
        string? tool,
        string? fingerprint,
        int limit) =>
        List(workspacePath, tool, fingerprint, category: null, projectId: null, app: null,
                taskId: null, latestOnly: false, limit)
            .Select(v => v.Record)
            .ToList();

    public static FailureRecord? Get(string workspacePath, string id)
    {
        var dir = FailuresDir(workspacePath);
        if (!Directory.Exists(dir))
            return null;
        return Directory.EnumerateFiles(dir, "*.jsonl")
            .SelectMany(ReadFile)
            .FirstOrDefault(r => string.Equals(r.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string? ResolveSuggestedNext(FailureRecord r)
    {
        if (!string.IsNullOrWhiteSpace(r.SuggestedNext))
            return r.SuggestedNext.Trim();
        if (!string.IsNullOrWhiteSpace(r.CorrectArgs))
            return "retry with correctArgs from journal";
        if (!string.IsNullOrWhiteSpace(r.Resolution))
            return r.Resolution.Trim();

        var cat = r.Category ?? "unknown";
        var err = r.ErrorOrMiss ?? "";
        if (cat == "incorrect_invocation")
            return $"man tool={r.Tool}; fix required args before retry";
        if (cat == "missing_precondition")
            return "satisfy precondition (status / reload / kill / index) then retry";
        if (cat == "environment" ||
            err.Contains("lock", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("being used by another process", StringComparison.OrdinalIgnoreCase))
            return "stop locking process (aid-publish -KillRunning / disable MCP) then retry";
        if (cat == "tool_bug")
            return "file tool bug; do not keep retrying same args";
        return null;
    }

    private static bool NoMetaPatch(
        string? category, string? projectId, string? app, string? taskId, string? argsTried) =>
        string.IsNullOrWhiteSpace(category) &&
        string.IsNullOrWhiteSpace(projectId) &&
        string.IsNullOrWhiteSpace(app) &&
        string.IsNullOrWhiteSpace(taskId) &&
        string.IsNullOrWhiteSpace(argsTried);

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() : TrimOrNull(fallback);

    private static string? TrimOrNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static IEnumerable<FailureRecord> ReadTool(string workspacePath, string tool) =>
        ReadFile(FileForTool(workspacePath, tool));

    private static IEnumerable<FailureRecord> ReadFile(string path)
    {
        foreach (var line in JsonlExclusiveAppend.ReadAllLinesShared(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            FailureRecord? rec = null;
            try
            {
                rec = JsonSerializer.Deserialize<FailureRecord>(line, JsonIn);
            }
            catch (JsonException)
            {
                // skip corrupt line
            }

            if (rec is not null)
                yield return rec;
        }
    }

    private static string? RedactArgs(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return null;
        var s = args.Trim();
        if (s.Length > 2000)
            s = s[..2000] + "…";
        return s;
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex SafeToolChars();
}
