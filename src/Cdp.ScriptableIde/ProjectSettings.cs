namespace Cdp.ScriptableIde;

/// <summary>
/// Per-project conventions (language-aware harness fills Detect).
/// Primary store: <c>.cdp/project.toml</c> under work root; rare escape = per-call overrides.
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>Pinned test FW when set; null ⇒ follow <see cref="TestFrameworkPolicy"/>.</summary>
    public TestFrameworkKind? TestFramework { get; set; }

    public TestFrameworkPolicy TestFrameworkPolicy { get; set; } = TestFrameworkPolicy.Detect;

    /// <summary>Where current TestFramework came from (file / detect / set).</summary>
    public string? TestFrameworkSource { get; set; }

    /// <summary>Placeholder — python docstring / jsdoc style later.</summary>
    public string? DocstringStyle { get; set; }

    /// <summary>Placeholder — roslyn cleanup / format profile later.</summary>
    public string? FormatProfile { get; set; }

    public string? SettingsSource { get; set; }

    public string? CanonLang { get; set; }

    public string? OrgStyle { get; set; }

    public string? OrgStyleRoot { get; set; }

    public string CanonFile { get; set; } = "canon.md";

    public int CanonPreviewLines { get; set; } = 12;

    public int CanonBudgetPersonal { get; set; } = 500;

    public int CanonBudgetOrgCore { get; set; } = 600;

    public int CanonBudgetOrgLang { get; set; } = 800;

    public int CanonBudgetOrgLangDesign { get; set; } = 600;

    public int CanonBudgetProject { get; set; } = 1500;

    public string OperatorPrefsRelpath { get; set; } = "knowledge/personal/operator-writing-prefs.md";

    public string OrgCoreFile { get; set; } = "principles.md";

    public string OrgLangFile { get; set; } = "writing-surface.md";

    public string OrgLangDesignFile { get; set; } = "design-patterns.md";

    public string SettingsPath { get; set; } = "";
}

public static class ProjectSettingsPaths
{
    public const string RelDir = ".cdp";
    public const string RelFile = ".cdp/project.toml";
    public const string AltFile = "cdp-project.toml";

    public static string ResolveFile(string workRoot)
    {
        var primary = Path.Combine(workRoot, RelFile.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(primary))
            return primary;
        var alt = Path.Combine(workRoot, AltFile);
        if (File.Exists(alt))
            return alt;
        return primary;
    }
}
