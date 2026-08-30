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

        var (effectiveLang, langSource) = ResolveEffectiveLang(settings, host);

        var (styleRoot, styleSource) = ResolveOrgStyleRoot(settings, host);
        if (settings.OrgStyle is not null)
            styleSource = $"{styleSource};org_style={settings.OrgStyle}";

        var orgCorePath = ResolveOrgCorePath(settings, styleRoot);
        codeEntries.Add(BuildEntry(
            "org-core",
            WritingCanonPlane.Code,
            orgCorePath,
            settings.BudgetOrgCore,
            settings.PreviewLines,
            styleSource));

        if (!string.IsNullOrWhiteSpace(effectiveLang))
        {
            var orgLangPath = ResolveOrgLangPath(effectiveLang, settings, styleRoot);
            codeEntries.Add(BuildEntry(
                "org-lang",
                WritingCanonPlane.Code,
                orgLangPath,
                settings.BudgetOrgLang,
                settings.PreviewLines,
                styleSource));

            var orgDesignPath = ResolveOrgLangDesignPath(effectiveLang, settings, styleRoot);
            codeEntries.Add(BuildEntry(
                "org-lang-design",
                WritingCanonPlane.Code,
                orgDesignPath,
                settings.BudgetOrgLangDesign,
                settings.PreviewLines,
                styleSource));
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
            effectiveLang,
            langSource,
            operatorEntries,
            codeEntries);
    }

    private static (string? Lang, string Source) ResolveEffectiveLang(
        ProjectCanonSettings settings,
        WritingCanonHostPaths host)
    {
        if (!string.IsNullOrWhiteSpace(settings.Lang))
            return (settings.Lang.Trim(), "project.toml");

        if (!string.IsNullOrWhiteSpace(host.SessionLanguage)
            && IsCodeLanguage(host.SessionLanguage))
            return (host.SessionLanguage.Trim(), "session");

        if (!string.IsNullOrWhiteSpace(host.BufferLanguage)
            && IsCodeLanguage(host.BufferLanguage))
            return (host.BufferLanguage.Trim(), "buffer");

        return (null, "unset");
    }

    private static bool IsCodeLanguage(string language) =>
        language.Trim().ToLowerInvariant() is "csharp" or "typescript" or "python" or "powershell" or "delphi";

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

    private static string ResolveOrgCorePath(ProjectCanonSettings settings, string? styleRoot)
    {
        var file = settings.OrgCoreFile.Trim();
        if (string.IsNullOrWhiteSpace(styleRoot))
            return Path.Combine("(unset)", "core", file);
        return Path.Combine(styleRoot, "core", file);
    }

    private static string ResolveOrgLangPath(
        string lang,
        ProjectCanonSettings settings,
        string? styleRoot)
    {
        var file = settings.OrgLangFile.Trim();
        if (string.IsNullOrWhiteSpace(styleRoot))
            return Path.Combine("(unset)", lang, file);
        return Path.Combine(styleRoot, lang.Trim(), file);
    }

    private static string ResolveOrgLangDesignPath(
        string lang,
        ProjectCanonSettings settings,
        string? styleRoot)
    {
        var file = settings.OrgLangDesignFile.Trim();
        if (string.IsNullOrWhiteSpace(styleRoot))
            return Path.Combine("(unset)", lang, file);
        return Path.Combine(styleRoot, lang.Trim(), file);
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

    private static ProjectCanonSettings FromToml(AIGuiders.Platform.Configurations.Project.ProjectCanonSettings? canon) =>
        new()
        {
            Lang = canon?.Lang,
            OrgStyle = canon?.OrgStyle,
            OrgStyleRoot = canon?.OrgStyleRoot,
            CanonFile = canon?.CanonFile ?? "canon.md",
            PreviewLines = canon?.PreviewLines ?? 12,
            BudgetPersonal = canon?.BudgetPersonal ?? 500,
            BudgetOrgCore = canon?.BudgetOrgCore ?? 600,
            BudgetOrgLang = canon?.BudgetOrgLang ?? 800,
            BudgetOrgLangDesign = canon?.BudgetOrgLangDesign ?? 600,
            BudgetProject = canon?.BudgetProject ?? 1500,
            OperatorPrefsRelpath = canon?.OperatorPrefsRelpath ?? "knowledge/personal/operator-writing-prefs.md",
            OrgCoreFile = canon?.OrgCoreFile ?? "principles.md",
            OrgLangFile = canon?.OrgLangFile ?? "writing-surface.md",
            OrgLangDesignFile = canon?.OrgLangDesignFile ?? "design-patterns.md",
        };
}
