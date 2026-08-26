namespace Cdp.ScriptableIde;

/// <summary>Resolve writing canon stack (CDP-ADR-0207) from embedded defaults + disk project.toml.</summary>
public static class WritingCanonStackResolver
{
    public static WritingCanonStackResult Build(string scmRoot, WritingCanonHostPaths? host = null)
    {
        host ??= new WritingCanonHostPaths();
        var root = Path.GetFullPath(scmRoot.Trim());
        var (settings, settingsPath, settingsSource) = ProjectCanonSettingsLoader.LoadEffective(root);
        var operatorEntries = new List<WritingCanonStackEntry>();
        var codeEntries = new List<WritingCanonStackEntry>();

        var (personalPath, personalSource) = ResolveOperatorPrefsPath(settings, host);
        operatorEntries.Add(BuildEntry(
            "personal",
            WritingCanonPlane.Operator,
            personalPath,
            settings.BudgetPersonal,
            settings.PreviewLines,
            personalSource));

        if (!string.IsNullOrWhiteSpace(settings.Lang))
        {
            var (orgLangPath, orgSource) = ResolveOrgLangPath(settings, host);
            if (settings.OrgStyle is not null)
                orgSource = $"{orgSource};org_style={settings.OrgStyle}";
            codeEntries.Add(BuildEntry(
                "org-lang",
                WritingCanonPlane.Code,
                orgLangPath,
                settings.BudgetOrgLang,
                settings.PreviewLines,
                orgSource));
        }

        var projectCanonPath = ResolveProjectCanonPath(root, settings);
        codeEntries.Add(BuildEntry(
            "project",
            WritingCanonPlane.Code,
            projectCanonPath,
            settings.BudgetProject,
            settings.PreviewLines,
            File.Exists(settingsPath) ? "disk+embedded" : "embedded"));

        return new WritingCanonStackResult(
            root,
            settingsPath,
            settingsSource,
            operatorEntries,
            codeEntries);
    }

    private static WritingCanonStackEntry BuildEntry(
        string layer,
        WritingCanonPlane plane,
        string path,
        int budget,
        int previewLines,
        string source)
    {
        var exists = File.Exists(path);
        return new WritingCanonStackEntry(
            layer,
            plane,
            path,
            exists,
            budget,
            exists ? ReadPreview(path, previewLines) : null,
            source);
    }

    private static string ResolveProjectCanonPath(string scmRoot, ProjectCanonSettings settings) =>
        Path.Combine(scmRoot, ProjectSettingsPaths.RelDir, settings.CanonFile);

    private static (string Path, string Source) ResolveOrgLangPath(
        ProjectCanonSettings settings,
        WritingCanonHostPaths host)
    {
        var lang = settings.Lang!.Trim();
        var file = settings.OrgLangFile.Trim();
        var (styleRoot, source) = ResolveOrgStyleRoot(settings, host);
        if (string.IsNullOrWhiteSpace(styleRoot))
            return (Path.Combine("(unset)", lang, file), "unset");
        return (Path.Combine(styleRoot, lang, file), source);
    }

    private static (string? Root, string Source) ResolveOrgStyleRoot(
        ProjectCanonSettings settings,
        WritingCanonHostPaths host)
    {
        if (!string.IsNullOrWhiteSpace(settings.OrgStyleRoot))
            return (Path.GetFullPath(settings.OrgStyleRoot.Trim()), "project.toml");

        if (!string.IsNullOrWhiteSpace(host.GuidersStyleRoot))
            return (Path.GetFullPath(host.GuidersStyleRoot.Trim()), "cdp-mcp.toml");

        return (null, "unset");
    }

    private static (string Path, string Source) ResolveOperatorPrefsPath(
        ProjectCanonSettings settings,
        WritingCanonHostPaths host)
    {
        var rel = settings.OperatorPrefsRelpath.Replace('/', Path.DirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(host.PrimaryKnowledgeRoot))
            return (
                Path.Combine(host.PrimaryKnowledgeRoot.Trim(), rel),
                "agent-notes-mcp.toml+embedded");

        return (rel, "embedded-only");
    }

    private static string ReadPreview(string path, int maxLines)
    {
        try
        {
            var lines = File.ReadLines(path).Take(maxLines).ToList();
            return string.Join(Environment.NewLine, lines);
        }
        catch
        {
            return "";
        }
    }
}

internal static class ProjectCanonSettingsLoader
{
    internal static (ProjectCanonSettings Settings, string SettingsPath, string SettingsSource) LoadEffective(string workRoot)
    {
        var settingsPath = ProjectSettingsPaths.ResolveFile(workRoot);
        var embedded = CdpProjectToml.DeserializeEmbedded();
        var disk = CdpProjectToml.TryDeserializeFile(settingsPath);
        var merged = CdpProjectToml.Merge(embedded, disk);
        var source = disk is null ? "embedded" : "embedded+disk";
        return (FromToml(merged.Canon), settingsPath, source);
    }

    private static ProjectCanonSettings FromToml(CdpProjectToml.ProjectTomlCanon? canon) =>
        new()
        {
            Lang = canon?.Lang,
            OrgStyle = canon?.OrgStyle,
            OrgStyleRoot = canon?.OrgStyleRoot,
            CanonFile = canon?.CanonFile ?? "canon.md",
            PreviewLines = canon?.PreviewLines ?? 12,
            BudgetPersonal = canon?.BudgetPersonal ?? 500,
            BudgetOrgLang = canon?.BudgetOrgLang ?? 800,
            BudgetProject = canon?.BudgetProject ?? 1500,
            OperatorPrefsRelpath = canon?.OperatorPrefsRelpath ?? "knowledge/personal/operator-writing-prefs.md",
            OrgLangFile = canon?.OrgLangFile ?? "writing-surface.md",
        };
}
