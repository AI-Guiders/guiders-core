#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalMcp.Core;

/// <summary>
/// File-backed durable job queue (ADR-0032 layer 3a). SSOT: %LocalAppData%/cdp-mcp/jobs/.
/// MCP/terminal enqueue + poll; out-of-process supervisor claims and runs.
/// </summary>
public static class DurableJobStore
{
    public const string WireSchema = "lifecycle_job/v0";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static string? RootOverrideForTests { get; set; }

    public static string JobsDirectory
    {
        get
        {
            var root = RootOverrideForTests
                       ?? Path.Combine(
                           Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "cdp-mcp");
            return Path.Combine(root, "jobs");
        }
    }

    static string JobPath(string jobId) => Path.Combine(JobsDirectory, jobId + ".json");
    static string QueuePath => Path.Combine(JobsDirectory, "queue.jsonl");

    public static string EnqueueShell(
        string? command,
        string[]? argv,
        string? tab,
        string? cwd,
        string? shell,
        int? timeoutSeconds,
        int? codepage,
        string? armId = null,
        JsonSerializerOptions? pretty = null)
    {
        pretty ??= JsonOpts;
        EnsureDirectory();
        var tabSafe = string.IsNullOrWhiteSpace(tab) ? "main" : tab.Trim();
        var jobId = $"shell-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var commandLabel = command ?? (argv is { Length: > 0 } ? string.Join(' ', argv) : null);

        var record = new DurableJobRecord
        {
            JobId = jobId,
            Kind = "shell",
            IgniteEvent = "shell_finished",
            State = "queued",
            ArmId = armId,
            EnqueuedUtc = DateTimeOffset.UtcNow,
            Shell = new DurableShellPayload
            {
                Command = command,
                Argv = argv,
                Tab = tabSafe,
                Cwd = cwd,
                Shell = shell,
                TimeoutSeconds = timeoutSeconds,
                Codepage = codepage
            }
        };

        WriteRecord(record);
        AppendQueue("enqueue", jobId);

        return JsonSerializer.Serialize(new
        {
            schema = WireSchema,
            ok = true,
            durable = true,
            state = "queued",
            job_id = jobId,
            kind = "shell",
            ignite_event = "shell_finished",
            tab = tabSafe,
            command = commandLabel,
            enqueued_utc = record.EnqueuedUtc,
            hint = "Durable job queued — supervisor runs out-of-process. Poll terminal_job_last; wake on shell_finished."
        }, pretty);
    }

    public static string? TryGetInFlightKind(string kind)
    {
        var hit = ListRecords()
            .Where(r => r.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
                        && r.State is "queued" or "running")
            .OrderByDescending(r => r.EnqueuedUtc)
            .FirstOrDefault();
        return hit?.JobId;
    }

    public static string EnqueueLifecycle(
        string kind,
        string igniteEvent,
        DurableLifecyclePayload lifecycle,
        string? targetHint,
        string? armId = null,
        JsonSerializerOptions? pretty = null)
    {
        pretty ??= JsonOpts;
        EnsureDirectory();

        if (TryGetInFlightKind(kind) is { } inFlight)
        {
            return JsonSerializer.Serialize(new
            {
                schema = WireSchema,
                ok = false,
                error = $"{kind}_in_flight",
                job_id = inFlight,
                kind,
                state = "queued",
                hint = $"Another durable {kind} job is queued/running — poll cdp_lifecycle_last kind={kind}."
            }, pretty);
        }

        var jobId = $"{kind}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var record = new DurableJobRecord
        {
            JobId = jobId,
            Kind = kind,
            IgniteEvent = igniteEvent,
            State = "queued",
            ArmId = armId,
            EnqueuedUtc = DateTimeOffset.UtcNow,
            Lifecycle = lifecycle
        };

        WriteRecord(record);
        AppendQueue("enqueue", jobId);

        return JsonSerializer.Serialize(new
        {
            schema = WireSchema,
            ok = true,
            durable = true,
            state = "queued",
            job_id = jobId,
            kind,
            ignite_event = igniteEvent,
            target = targetHint,
            enqueued_utc = record.EnqueuedUtc,
            hint = $"Durable {kind} queued — supervisor runs CdpMcp out-of-process (RID-aware). Poll cdp_lifecycle_last; wake on {igniteEvent}."
        }, pretty);
    }

    public static string Scene(JsonSerializerOptions? pretty = null)
    {
        pretty ??= JsonOpts;
        var items = ListRecords()
            .OrderByDescending(r => r.FinishedUtc ?? r.StartedUtc ?? r.EnqueuedUtc)
            .Take(24)
            .Select(r => new
            {
                job_id = r.JobId,
                kind = r.Kind,
                state = r.State,
                ignite_event = r.IgniteEvent,
                tab = r.Shell?.Tab,
                enqueued_utc = r.EnqueuedUtc,
                started_utc = r.StartedUtc,
                finished_utc = r.FinishedUtc,
                error = r.Error,
                claimed_by = r.ClaimedBy
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            schema = WireSchema,
            ok = true,
            jobs = items,
            hint = "Poll cdp_lifecycle_last / terminal_job_last by kind or job_id."
        }, pretty);
    }

    public static string Last(string? jobId, string? kind, JsonSerializerOptions? pretty = null)
    {
        pretty ??= JsonOpts;
        DurableJobRecord? record = null;
        if (!string.IsNullOrWhiteSpace(jobId))
            TryReadRecord(jobId.Trim(), out record);
        if (record is null && !string.IsNullOrWhiteSpace(kind))
        {
            record = ListRecords()
                .Where(r => r.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.FinishedUtc ?? r.StartedUtc ?? r.EnqueuedUtc)
                .FirstOrDefault();
        }

        if (record is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = WireSchema,
                ok = false,
                error = "job_not_found",
                hint = "Pass kind=shell or job_id= from enqueue response."
            }, pretty);
        }

        if (record.State is "queued" or "running")
        {
            return JsonSerializer.Serialize(new
            {
                schema = WireSchema,
                ok = true,
                durable = true,
                state = record.State,
                job_id = record.JobId,
                kind = record.Kind,
                ignite_event = record.IgniteEvent,
                tab = record.Shell?.Tab,
                enqueued_utc = record.EnqueuedUtc,
                started_utc = record.StartedUtc,
                hint = record.State == "queued"
                    ? "Waiting for supervisor claim."
                    : "Running in supervisor — poll again or wait for shell_finished wake."
            }, pretty);
        }

        if (!string.IsNullOrEmpty(record.ResultJson))
            return record.ResultJson;

        return JsonSerializer.Serialize(new
        {
            schema = WireSchema,
            ok = false,
            durable = true,
            state = record.State,
            job_id = record.JobId,
            kind = record.Kind,
            error = record.Error ?? "failed",
            enqueued_utc = record.EnqueuedUtc,
            finished_utc = record.FinishedUtc
        }, pretty);
    }

    /// <summary>Supervisor: claim oldest queued job (FIFO).</summary>
    public static DurableJobRecord? TryClaimNext(string supervisorId)
    {
        using var gate = AcquireGate();
        if (gate is null)
            return null;

        try
        {
            var candidate = ListRecordsUnlocked()
                .Where(r => r.State == "queued")
                .OrderBy(r => r.EnqueuedUtc)
                .FirstOrDefault();
            if (candidate is null)
                return null;

            if (!TryReadRecordUnlocked(candidate.JobId, out var live) || live.State != "queued")
                return null;

            live.State = "running";
            live.StartedUtc = DateTimeOffset.UtcNow;
            live.ClaimedBy = supervisorId;
            WriteRecordUnlocked(live);
            AppendQueueUnlocked("claim", live.JobId, supervisorId);
            return live;
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    public static void Finish(string jobId, bool ok, string? resultJson, string? error = null)
    {
        using var gate = AcquireGate();
        if (gate is null)
            return;

        try
        {
            if (!TryReadRecordUnlocked(jobId, out var record))
                return;

            record.State = ok ? "idle" : "failed";
            record.ResultJson = resultJson;
            record.Error = error;
            record.FinishedUtc = DateTimeOffset.UtcNow;
            WriteRecordUnlocked(record);
            AppendQueueUnlocked(ok ? "finish_ok" : "finish_fail", jobId);
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    static void EnsureDirectory() => Directory.CreateDirectory(JobsDirectory);

    static List<DurableJobRecord> ListRecords()
    {
        using var gate = AcquireGate();
        if (gate is null)
            return [];
        try
        {
            return ListRecordsUnlocked();
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    static List<DurableJobRecord> ListRecordsUnlocked()
    {
        EnsureDirectory();
        var list = new List<DurableJobRecord>();
        foreach (var path in Directory.EnumerateFiles(JobsDirectory, "*.json"))
        {
            try
            {
                var raw = File.ReadAllText(path);
                var rec = JsonSerializer.Deserialize<DurableJobRecord>(raw, JsonOpts);
                if (rec is not null)
                    list.Add(rec);
            }
            catch
            {
                /* skip corrupt */
            }
        }

        return list;
    }

    public static bool TryReadRecordPublic(string jobId, out DurableJobRecord record)
        => TryReadRecord(jobId, out record);

    static bool TryReadRecord(string jobId, out DurableJobRecord record)
    {
        using var gate = AcquireGate();
        if (gate is null)
        {
            record = null!;
            return false;
        }

        try
        {
            return TryReadRecordUnlocked(jobId, out record);
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    static bool TryReadRecordUnlocked(string jobId, out DurableJobRecord record)
    {
        record = null!;
        var path = JobPath(jobId);
        if (!File.Exists(path))
            return false;
        try
        {
            var raw = File.ReadAllText(path);
            var rec = JsonSerializer.Deserialize<DurableJobRecord>(raw, JsonOpts);
            if (rec is null)
                return false;
            record = rec;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void WriteRecord(DurableJobRecord record)
    {
        using var gate = AcquireGate();
        if (gate is null)
            return;
        try
        {
            WriteRecordUnlocked(record);
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    static void WriteRecordUnlocked(DurableJobRecord record)
    {
        EnsureDirectory();
        var path = JobPath(record.JobId);
        var json = JsonSerializer.Serialize(record, JsonOpts);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    static void AppendQueue(string evt, string jobId, string? detail = null)
    {
        using var gate = AcquireGate();
        if (gate is null)
            return;
        try
        {
            AppendQueueUnlocked(evt, jobId, detail);
        }
        finally
        {
            gate.ReleaseMutex();
        }
    }

    static void AppendQueueUnlocked(string evt, string jobId, string? detail = null)
    {
        EnsureDirectory();
        var line = JsonSerializer.Serialize(new
        {
            ts_utc = DateTimeOffset.UtcNow,
            evt,
            job_id = jobId,
            detail
        });
        File.AppendAllText(QueuePath, line + Environment.NewLine);
    }

    static Mutex? AcquireGate()
    {
        var mutex = new Mutex(false, @"Global\CdpMcp.DurableJobs");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(12)))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            /* recovered */
        }

        return mutex;
    }
}
