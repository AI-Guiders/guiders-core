namespace Cdp.ScriptableIde;

public enum WritingCanonPlane
{
    Operator,
    Code,
}

public sealed record WritingCanonStackEntry(
    string Layer,
    WritingCanonPlane Plane,
    string Path,
    bool Exists,
    int Budget,
    string? Preview,
    string Source);

public sealed record WritingCanonStackResult(
    string ScmRoot,
    string SettingsPath,
    string SettingsSource,
    string? EffectiveLang,
    string LangSource,
    IReadOnlyList<WritingCanonStackEntry> Operator,
    IReadOnlyList<WritingCanonStackEntry> Code);

/// <summary>Host-level paths from CDP / Agent Notes TOML (not env).</summary>
public sealed class WritingCanonHostPaths
{
    /// <summary><c>[knowledge].primary</c> via <c>memory.notes_config</c> → agent-notes-mcp.toml.</summary>
    public string? PrimaryKnowledgeRoot { get; init; }

    /// <summary><c>[canon].guiders_style_root</c> in cdp-mcp.toml (project <c>org_style_root</c> wins).</summary>
    public string? GuidersStyleRoot { get; init; }

    /// <summary>CDP session language after <c>cdp_open</c> (project detect).</summary>
    public string? SessionLanguage { get; init; }

    /// <summary>Language inferred from open buffer paths (MRU scan).</summary>
    public string? BufferLanguage { get; init; }
}

public sealed class ProjectCanonSettings
{
    public string? Lang { get; init; }
    public string? OrgStyle { get; init; }
    public string? OrgStyleRoot { get; init; }
    public string CanonFile { get; init; } = "canon.md";
    public int PreviewLines { get; init; } = 12;
    public int BudgetPersonal { get; init; } = 500;
    public int BudgetOrgCore { get; init; } = 600;
    public int BudgetOrgLang { get; init; } = 800;
    public int BudgetOrgLangDesign { get; init; } = 600;
    public int BudgetProject { get; init; } = 1500;
    public string OperatorPrefsRelpath { get; init; } = "knowledge/personal/operator-writing-prefs.md";
    public string OrgCoreFile { get; init; } = "principles.md";
    public string OrgLangFile { get; init; } = "writing-surface.md";
    public string OrgLangDesignFile { get; init; } = "design-patterns.md";
}
