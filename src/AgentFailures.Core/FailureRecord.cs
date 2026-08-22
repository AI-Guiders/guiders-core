namespace AgentFailures.Core;

/// <summary>One line in the workspace failure journal.</summary>
public sealed record FailureRecord(
    string Id,
    DateTimeOffset AtUtc,
    string Tool,
    string Fingerprint,
    string? ErrorOrMiss,
    string? ArgsTried,
    string? Resolution,
    string? CorrectArgs,
    string? Why,
    int SeenCount,
    string? SeenBefore,
    string? TaskId,
    string? Category = null,
    string? ProjectId = null,
    string? App = null,
    string? SuggestedNext = null);

/// <summary>List row with resolved suggestedNext (stored or heuristic).</summary>
public sealed record FailureView(
    FailureRecord Record,
    string? SuggestedNext,
    bool Deduped = false);
