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
    IReadOnlyList<WritingCanonStackEntry> Operator,
    IReadOnlyList<WritingCanonStackEntry> Code);

public sealed class ProjectCanonSettings
{
    public string? Lang { get; init; }
    public string? OrgStyle { get; init; }
    public string? OrgStyleRoot { get; init; }
    public string CanonFile { get; init; } = "canon.md";
    public int PreviewLines { get; init; } = 12;
    public int BudgetPersonal { get; init; } = 500;
    public int BudgetOrgLang { get; init; } = 800;
    public int BudgetProject { get; init; } = 1500;
    public string OperatorPrefsRelpath { get; init; } = "knowledge/personal/operator-writing-prefs.md";
    public string OrgLangFile { get; init; } = "writing-surface.md";
}
