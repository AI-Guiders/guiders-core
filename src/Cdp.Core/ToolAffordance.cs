namespace Cdp.Core;

/// <summary>
/// Tool metadata for catalog = f(phase, object [, language]); intent ranks.
/// Language sits beside phase×object — not a free-text goal.
/// </summary>
public sealed record ToolAffordance(
    string PrefixedName,
    string Domain,
    string UnderlyingName,
    IReadOnlyList<CdpPhase> Phases,
    IReadOnlyList<CdpObjectKind> Objects,
    IReadOnlyList<CdpIntent> Intents,
    int Cost = 1,
    int Risk = 1,
    string? Hint = null,
    IReadOnlyList<string>? Languages = null)
{
    /// <summary>Empty/null Languages → <see cref="CdpLanguages.Any"/> (agnostic).</summary>
    public IReadOnlyList<string> EffectiveLanguages =>
        Languages is { Count: > 0 } ? Languages : [CdpLanguages.Any];
}

public sealed record CatalogHit(ToolAffordance Affordance, int Score);

public sealed class SessionContext
{
    public CdpPhase Phase { get; set; } = CdpPhase.Recall;
    public CdpObjectKind Object { get; set; } = CdpObjectKind.Kb;
    public CdpIntent? Intent { get; set; }
    /// <summary>Null or <see cref="CdpLanguages.Any"/> = no language filter. Canonical id from registry.</summary>
    public string? Language { get; set; }

    /// <summary>Set by <c>cdp_open</c> — directory used as project root.</summary>
    public string? ProjectRoot { get; set; }

    /// <summary>Detector kind: e.g. <c>sln</c>|<c>csproj</c>|<c>tsconfig</c>|<c>pyproject</c>|<c>any</c>.</summary>
    public string? ProjectKind { get; set; }

    /// <summary>Primary csharp anchor (.sln / .csproj) when opened.</summary>
    public string? SolutionOrProjectPath { get; set; }

    /// <summary>Primary tsconfig path when opened as typescript.</summary>
    public string? TsConfigPath { get; set; }

    /// <summary>
    /// Git toplevel for session project (<c>git rev-parse --show-toplevel</c> after <c>cdp_open</c>).
    /// CDP injects as default <c>workspace_path</c> for <c>git_*</c> when omitted.
    /// </summary>
    public string? ScmRoot { get; set; }

    public string ToJson() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            phase = CdpEnumParse.ToWire(Phase),
            @object = CdpEnumParse.ToWire(Object),
            intent = Intent is null ? null : CdpEnumParse.ToWire(Intent.Value),
            language = Language,
            project_root = ProjectRoot,
            project_kind = ProjectKind,
            solution_or_project_path = SolutionOrProjectPath,
            tsconfig_path = TsConfigPath,
            scm_root = ScmRoot
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public void CopyFrom(SessionContext other)
    {
        Phase = other.Phase;
        Object = other.Object;
        Intent = other.Intent;
        Language = other.Language;
        ProjectRoot = other.ProjectRoot;
        ProjectKind = other.ProjectKind;
        SolutionOrProjectPath = other.SolutionOrProjectPath;
        TsConfigPath = other.TsConfigPath;
        ScmRoot = other.ScmRoot;
    }

    public SessionContext Clone()
    {
        var c = new SessionContext();
        c.CopyFrom(this);
        return c;
    }
}
