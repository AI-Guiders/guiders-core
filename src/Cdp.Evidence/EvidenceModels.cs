using System.Text.Json.Serialization;

namespace Cdp.Evidence;

public static class EvidenceSchema
{
    public const string Version = "evidence/v0";
}

/// <summary>Source channel of the pipe being projected.</summary>
public enum EvidenceSource
{
    Auto,
    Build,
    Test,
    Publish,
    Csx,
    Shell,
    Roslyn,
    Generic
}

public sealed record EvidenceContext(
    string? ProjectRoot = null,
    string? SolutionOrProjectPath = null,
    Func<string, string>? RemapPath = null,
    int MaxItems = 24,
    int MaxResidualChars = 4_000,
    // When false (default): keep errors + at most MaxWarnings warnings.
    bool IncludeWarnings = false,
    int MaxWarnings = 3);

public sealed record EvidenceItem(
    string Severity,
    string Message,
    string? Id = null,
    string? Path = null,
    int? Line = null,
    int? Column = null,
    string? Anchor = null,
    string? Hint = null,
    string? Title = null);

public sealed record EvidenceDocument(
    string Schema,
    string Source,
    bool Ok,
    int ItemCount,
    IReadOnlyList<EvidenceItem> Items,
    string? Residual = null,
    string? Note = null)
{
    public static EvidenceDocument Empty(EvidenceSource source, string? note = null) =>
        new(EvidenceSchema.Version, SourceName(source), true, 0, [], Note: note);

    public static string SourceName(EvidenceSource s) => s switch
    {
        EvidenceSource.Auto => "auto",
        EvidenceSource.Build => "build",
        EvidenceSource.Test => "test",
        EvidenceSource.Publish => "publish",
        EvidenceSource.Csx => "csx",
        EvidenceSource.Shell => "shell",
        EvidenceSource.Roslyn => "roslyn",
        EvidenceSource.Generic => "generic",
        _ => "auto"
    };
}

/// <summary>DTO for JSON serialization (property names match evidence/v0).</summary>
public sealed class EvidenceDocumentDto
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = EvidenceSchema.Version;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "auto";

    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    [JsonPropertyName("items")]
    public List<EvidenceItemDto> Items { get; init; } = [];

    [JsonPropertyName("residual")]
    public string? Residual { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

public sealed class EvidenceItemDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "error";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("line")]
    public int? Line { get; init; }

    [JsonPropertyName("column")]
    public int? Column { get; init; }

    [JsonPropertyName("anchor")]
    public string? Anchor { get; init; }

    [JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}
