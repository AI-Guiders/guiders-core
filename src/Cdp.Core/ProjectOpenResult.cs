namespace Cdp.Core;

/// <summary>Result of language/open detect for <c>cdp_open</c>.</summary>
public sealed record ProjectOpenResult(
    string Root,
    string Kind,
    string Language,
    IReadOnlyList<string> Anchors,
    string? SolutionOrProjectPath = null,
    string? TsConfigPath = null);
