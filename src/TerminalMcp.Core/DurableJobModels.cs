#nullable enable

namespace TerminalMcp.Core;

public sealed class DurableJobRecord
{
    public string Schema { get; set; } = "durable_job/v0";
    public required string JobId { get; set; }
    public required string Kind { get; set; }
    public required string IgniteEvent { get; set; }
    public string State { get; set; } = "queued";
    public DurableShellPayload? Shell { get; set; }
    public DurableLifecyclePayload? Lifecycle { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public string? ArmId { get; set; }
    public DateTimeOffset EnqueuedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public string? ClaimedBy { get; set; }
}

public sealed class DurableShellPayload
{
    public string? Command { get; set; }
    public string[]? Argv { get; set; }
    public string? Tab { get; set; }
    public string? Cwd { get; set; }
    public string? Shell { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? Codepage { get; set; }
}

public sealed class DurableLifecyclePayload
{
    public string? ProjectRoot { get; set; }
    public string? ScmRoot { get; set; }
    public string? SolutionOrProjectPath { get; set; }
    public string? ProjectKind { get; set; }
    public string? TsConfigPath { get; set; }
    /// <summary>Enqueueing seat binary (RID-aware). Supervisor prefers this over path search.</summary>
    public string? WorkerExePath { get; set; }
    /// <summary>Seat that armed ignite (cdp|cdp-debug) — worker notify must target this store.</summary>
    public string? IgniteSeat { get; set; }
    public string ArgsJson { get; set; } = "{}";
}
