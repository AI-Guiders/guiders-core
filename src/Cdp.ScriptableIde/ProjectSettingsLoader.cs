using System.Text.Json;
using Tomlyn;

namespace Cdp.ScriptableIde;

/// <summary>Load/save <see cref="ProjectSettings"/>; defaults from embedded TOML + disk overlay.</summary>
public static class ProjectSettingsLoader
{
    private static readonly TomlSerializerOptions TomlOpts = CdpProjectToml.Options;

    /// <summary>Read embedded defaults + disk overlay (if any) then Detect-fill unset test FW.</summary>
    public static void Hydrate(PlanContext plan)
    {
        plan.Settings.SettingsPath = ProjectSettingsPaths.ResolveFile(plan.WorkRoot);
        ApplyMergedToml(plan.Settings, plan.WorkRoot);
        FillDetect(plan);
    }

    public static void FillDetect(PlanContext plan)
    {
        if (plan.Settings.TestFramework is not null)
            return;
        if (plan.Settings.TestFrameworkPolicy is TestFrameworkPolicy.Specified)
            return;

        try
        {
            var r = TestFrameworkResolver.Resolve(plan, plan.Settings.TestFrameworkPolicy, specified: null);
            plan.Settings.TestFramework = r.Kind;
            plan.Settings.TestFrameworkSource = r.Source;
        }
        catch
        {
            var fallback = TestFrameworkResolver.DefaultForLanguage(plan.Language ?? "csharp");
            plan.Settings.TestFramework = fallback;
            plan.Settings.TestFrameworkSource = "fallback";
        }
    }

    public static void TryReadFile(ProjectSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SettingsPath))
            return;
        ApplyMergedToml(settings, ResolveWorkRootFromSettingsPath(settings.SettingsPath));
    }

    internal static string ResolveWorkRootFromSettingsPath(string settingsPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? settingsPath;
        if (string.Equals(Path.GetFileName(dir), ProjectSettingsPaths.RelDir, StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(dir) ?? dir;
        return dir;
    }

    private static void ApplyMergedToml(ProjectSettings settings, string workRoot)
    {
        var embedded = CdpProjectToml.DeserializeEmbedded();
        var disk = CdpProjectToml.TryDeserializeFile(ProjectSettingsPaths.ResolveFile(workRoot));
        var doc = CdpProjectToml.Merge(embedded, disk);
        settings.SettingsSource = disk is null ? "embedded" : "embedded+disk";

        if (doc.Test is { } test)
        {
            if (!string.IsNullOrWhiteSpace(test.Policy)
                && Enum.TryParse<TestFrameworkPolicy>(test.Policy, ignoreCase: true, out var policy))
                settings.TestFrameworkPolicy = policy;

            if (!string.IsNullOrWhiteSpace(test.Framework)
                && TryParseFramework(test.Framework, out var kind))
            {
                settings.TestFramework = kind;
                settings.TestFrameworkPolicy = TestFrameworkPolicy.Specified;
                settings.TestFrameworkSource = "file";
            }
        }

        if (!string.IsNullOrWhiteSpace(doc.Docs?.Style))
            settings.DocstringStyle = doc.Docs.Style;

        if (!string.IsNullOrWhiteSpace(doc.Format?.Profile))
            settings.FormatProfile = doc.Format.Profile;

        if (doc.Canon is { } canon)
        {
            settings.CanonLang = canon.Lang;
            settings.OrgStyle = canon.OrgStyle;
            settings.OrgStyleRoot = canon.OrgStyleRoot;
            if (!string.IsNullOrWhiteSpace(canon.CanonFile))
                settings.CanonFile = canon.CanonFile;
            if (canon.PreviewLines is > 0)
                settings.CanonPreviewLines = canon.PreviewLines.Value;
            if (canon.BudgetPersonal is > 0)
                settings.CanonBudgetPersonal = canon.BudgetPersonal.Value;
            if (canon.BudgetOrgLang is > 0)
                settings.CanonBudgetOrgLang = canon.BudgetOrgLang.Value;
            if (canon.BudgetProject is > 0)
                settings.CanonBudgetProject = canon.BudgetProject.Value;
            if (!string.IsNullOrWhiteSpace(canon.OperatorPrefsRelpath))
                settings.OperatorPrefsRelpath = canon.OperatorPrefsRelpath;
            if (!string.IsNullOrWhiteSpace(canon.OrgLangFile))
                settings.OrgLangFile = canon.OrgLangFile;
        }
    }

    public static void Save(PlanContext plan)
    {
        var path = string.IsNullOrWhiteSpace(plan.Settings.SettingsPath)
            ? ProjectSettingsPaths.ResolveFile(plan.WorkRoot)
            : plan.Settings.SettingsPath;
        plan.Settings.SettingsPath = path;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var fw = plan.Settings.TestFramework?.ToString().ToLowerInvariant() ?? "";
        var policy = plan.Settings.TestFrameworkPolicy.ToString();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# CDP project settings (embedded defaults + disk overlay)");
        sb.AppendLine();
        sb.AppendLine("[test]");
        if (plan.Settings.TestFramework is not null
            && plan.Settings.TestFrameworkPolicy == TestFrameworkPolicy.Specified)
            sb.AppendLine($"framework = \"{TomlEscape(fw)}\"");
        sb.AppendLine($"policy = \"{TomlEscape(policy)}\"");
        sb.AppendLine();
        sb.AppendLine("[docs]");
        if (!string.IsNullOrWhiteSpace(plan.Settings.DocstringStyle))
            sb.AppendLine($"style = \"{TomlEscape(plan.Settings.DocstringStyle)}\"");
        else
            sb.AppendLine("# style = \"google\"");
        sb.AppendLine();
        sb.AppendLine("[format]");
        if (!string.IsNullOrWhiteSpace(plan.Settings.FormatProfile))
            sb.AppendLine($"profile = \"{TomlEscape(plan.Settings.FormatProfile)}\"");
        else
            sb.AppendLine("# profile = \"default\"");
        sb.AppendLine();
        sb.AppendLine("[canon]");
        if (!string.IsNullOrWhiteSpace(plan.Settings.CanonLang))
            sb.AppendLine($"lang = \"{TomlEscape(plan.Settings.CanonLang)}\"");
        if (!string.IsNullOrWhiteSpace(plan.Settings.OrgStyle))
            sb.AppendLine($"org_style = \"{TomlEscape(plan.Settings.OrgStyle)}\"");
        if (!string.IsNullOrWhiteSpace(plan.Settings.OrgStyleRoot))
            sb.AppendLine($"org_style_root = \"{TomlEscape(plan.Settings.OrgStyleRoot)}\"");
        if (!string.IsNullOrWhiteSpace(plan.Settings.CanonFile) && plan.Settings.CanonFile != "canon.md")
            sb.AppendLine($"canon_file = \"{TomlEscape(plan.Settings.CanonFile)}\"");

        File.WriteAllText(path, sb.ToString());
        if (plan.Settings.TestFrameworkPolicy == TestFrameworkPolicy.Specified)
            plan.Settings.TestFrameworkSource = "file";
    }

    public static bool TryParseFramework(string text, out TestFrameworkKind kind)
    {
        var raw = text.Trim();
        if (Enum.TryParse(raw, ignoreCase: true, out kind))
            return true;

        var t = raw.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        switch (t)
        {
            case "xunit":
                kind = TestFrameworkKind.Xunit;
                return true;
            case "nunit":
                kind = TestFrameworkKind.NUnit;
                return true;
            case "mstest":
                kind = TestFrameworkKind.MSTest;
                return true;
            case "jest":
                kind = TestFrameworkKind.Jest;
                return true;
            case "vitest":
                kind = TestFrameworkKind.Vitest;
                return true;
            case "nodetest":
            case "node:test":
            case "node":
                kind = TestFrameworkKind.NodeTest;
                return true;
            case "pytest":
                kind = TestFrameworkKind.Pytest;
                return true;
            case "unittest":
                kind = TestFrameworkKind.Unittest;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string TomlEscape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
