namespace AgentFindings.Core;

/// <summary>One artifact-memo line in the workspace findings journal.</summary>
public sealed record FindingRecord(
    string Id,
    DateTimeOffset AtUtc,
    string Path,
    string ContentHash,
    string? Relevance,
    string? Disposition,
    string? Summary,
    string? Anchors,
    IReadOnlyList<string>? DependsOnPaths,
    IReadOnlyDictionary<string, string>? DependsOnHashes,
    IReadOnlyList<string>? TaskIds,
    string Status,
    string? SessionId);

/// <summary>
/// One task-DAG revision. Workflow node = part of system + change intent — not KB / not project card.
/// Local AS IS + task TO BE + why.
/// </summary>
public sealed record TaskRecord(
    string Id,
    DateTimeOffset AtUtc,
    string TaskId,
    string? Title,
    string? AsIs,
    string? ToBe,
    string? Why,
    IReadOnlyList<string>? BlockedBy,
    IReadOnlyList<string>? Unlocks,
    IReadOnlyList<string>? MemberPaths,
    string Status,
    string? SessionId);

/// <summary>Result of comparing stored memo (+ deps) to current file bytes.</summary>
public sealed record FindingFreshness(
    string Path,
    string? CurrentHash,
    FindingRecord? Memo,
    bool? HashMatch,
    bool? DepsOk,
    IReadOnlyList<string>? StaleDeps,
    string Advice);

/// <summary>Task listing row with DAG-derived effective status.</summary>
public sealed record TaskView(
    TaskRecord Task,
    string EffectiveStatus,
    IReadOnlyList<string>? WaitingOn);
