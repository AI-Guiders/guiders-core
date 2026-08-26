namespace Cdp.ScriptableIde;

/// <summary>Resolve writing canon stack (CDP-ADR-0207) from embedded defaults + disk project.toml.</summary>
public static class WritingCanonStackResolver
{
    private const string EnvAgentNotesFile = "AGENT_NOTES_FILE";
    private const string EnvGuidersStyleRoot = "GUIDERS_STYLE_ROOT";
    private const string EnvOperatorWritingPrefs = "OPERATOR_WRITING_PREFS_PATH";

    public static WritingCanonStackResult Build(string scmRoot)
    {
        var root = Path.GetFullPath(scmRoot.Trim());
        var (settings, settingsPath, settingsSource) = ProjectCanonSettingsLoader.LoadEffective(root);
        var operatorEntries = new List<WritingCanonStackEntry>();
        var codeEntries = new List<WritingCanonStackEntry>();

        var personalPath = ResolveOperatorPrefsPath(settings);
        operatorEntries.Add(BuildEntry(
            "personal",
            WritingCanonPlane.Operator,
            personalPath,
            settings.BudgetPersonal,
            settings.PreviewLines,
            "primary-canon"));

        if (!string.IsNullOrWhiteSpace(settings.Lang))
        {
            var orgLangPath = ResolveOrgLangPath(settings);
            codeEntries.Add(BuildEntry(
                "org-lang",
                WritingCanonPlane.Code,
                orgLangPath,
                settings.BudgetOrgLang,
                settings.PreviewLines,
                settings.OrgStyle is not null ? $"org_style={settings.OrgStyle}" : "embedded"));
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

    private static string ResolveOrgLangPath(ProjectCanonSettings settings)
    {
        var lang = settings.Lang!.Trim();
        var file = settings.OrgLangFile.Trim();
        var styleRoot = ResolveOrgStyleRoot(settings);
        if (string.IsNullOrWhiteSpace(styleRoot))
            return Path.Combine("(unset)", lang, file);
        return Path.Combine(styleRoot, lang, file);
    }

    private static string? ResolveOrgStyleRoot(ProjectCanonSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.OrgStyleRoot))
            return Path.GetFullPath(settings.OrgStyleRoot.Trim());

        var env = Environment.GetEnvironmentVariable(EnvGuidersStyleRoot);
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env.Trim());

        return null;
    }

    private static string ResolveOperatorPrefsPath(ProjectCanonSettings settings)
    {
        var overridePath = Environment.GetEnvironmentVariable(EnvOperatorWritingPrefs);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath.Trim());

        var knowledgeRoot = TryInferKnowledgeRootFromAgentNotes();
        if (knowledgeRoot is not null)
            return Path.Combine(knowledgeRoot, settings.OperatorPrefsRelpath.Replace('/', Path.DirectorySeparatorChar));

        return settings.OperatorPrefsRelpath;
    }

    private static string? TryInferKnowledgeRootFromAgentNotes()
    {
        var notesFile = Environment.GetEnvironmentVariable(EnvAgentNotesFile);
        if (string.IsNullOrWhiteSpace(notesFile))
            return null;

        var current = Path.GetDirectoryName(Path.GetFullPath(notesFile.Trim()));
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "knowledge")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }

        return null;
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
