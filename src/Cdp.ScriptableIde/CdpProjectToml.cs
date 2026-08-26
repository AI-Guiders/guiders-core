using System.Text;
using System.Text.Json;
using Tomlyn;

namespace Cdp.ScriptableIde;

internal static class CdpProjectToml
{
    internal static readonly TomlSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal const string EmbeddedDefaultsResource = "cdp-project.defaults.toml";

    internal static ProjectTomlDocument DeserializeEmbedded() =>
        Deserialize<ProjectTomlDocument>(
            ReadEmbeddedRequired(EmbeddedDefaultsResource),
            EmbeddedDefaultsResource);

    internal static ProjectTomlDocument? TryDeserializeFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        return Deserialize<ProjectTomlDocument>(File.ReadAllText(path, Encoding.UTF8), path);
    }

    internal static ProjectTomlDocument Merge(ProjectTomlDocument embedded, ProjectTomlDocument? overlay) =>
        overlay is null ? embedded : overlay.MergeOver(embedded);

    private static T Deserialize<T>(string text, string label) where T : class
    {
        try
        {
            return TomlSerializer.Deserialize<T>(text, Options)
                ?? throw new InvalidOperationException($"Invalid TOML ({label}): empty document.");
        }
        catch (TomlException ex)
        {
            throw new InvalidOperationException($"Invalid TOML ({label}): {ex.Message}", ex);
        }
    }

    private static string ReadEmbeddedRequired(string resourceRelativePath)
    {
        if (!BundledScriptableIdeContent.TryReadEmbeddedText(resourceRelativePath, out var text))
            throw new InvalidOperationException($"Embedded TOML is missing: {resourceRelativePath}");
        return text;
    }

    internal sealed class ProjectTomlDocument
    {
        public ProjectTomlTest? Test { get; set; }
        public ProjectTomlDocs? Docs { get; set; }
        public ProjectTomlFormat? Format { get; set; }
        public ProjectTomlCanon? Canon { get; set; }

        internal ProjectTomlDocument MergeOver(ProjectTomlDocument defaults)
        {
            return new ProjectTomlDocument
            {
                Test = MergeTest(defaults.Test, Test),
                Docs = MergeDocs(defaults.Docs, Docs),
                Format = MergeFormat(defaults.Format, Format),
                Canon = MergeCanon(defaults.Canon, Canon),
            };
        }

        private static ProjectTomlTest? MergeTest(ProjectTomlTest? d, ProjectTomlTest? o) =>
            o is null ? d : new ProjectTomlTest
            {
                Framework = o.Framework ?? d?.Framework,
                Policy = o.Policy ?? d?.Policy,
            };

        private static ProjectTomlDocs? MergeDocs(ProjectTomlDocs? d, ProjectTomlDocs? o) =>
            o is null ? d : new ProjectTomlDocs { Style = o.Style ?? d?.Style };

        private static ProjectTomlFormat? MergeFormat(ProjectTomlFormat? d, ProjectTomlFormat? o) =>
            o is null ? d : new ProjectTomlFormat { Profile = o.Profile ?? d?.Profile };

        private static ProjectTomlCanon? MergeCanon(ProjectTomlCanon? d, ProjectTomlCanon? o)
        {
            if (o is null)
                return d;
            if (d is null)
                return o;
            return new ProjectTomlCanon
            {
                Lang = o.Lang ?? d.Lang,
                OrgStyle = o.OrgStyle ?? d.OrgStyle,
                OrgStyleRoot = o.OrgStyleRoot ?? d.OrgStyleRoot,
                CanonFile = o.CanonFile ?? d.CanonFile,
                PreviewLines = o.PreviewLines ?? d.PreviewLines,
                BudgetPersonal = o.BudgetPersonal ?? d.BudgetPersonal,
                BudgetOrgLang = o.BudgetOrgLang ?? d.BudgetOrgLang,
                BudgetProject = o.BudgetProject ?? d.BudgetProject,
                OperatorPrefsRelpath = o.OperatorPrefsRelpath ?? d.OperatorPrefsRelpath,
                OrgLangFile = o.OrgLangFile ?? d.OrgLangFile,
            };
        }
    }

    internal sealed class ProjectTomlTest
    {
        public string? Framework { get; set; }
        public string? Policy { get; set; }
    }

    internal sealed class ProjectTomlDocs
    {
        public string? Style { get; set; }
    }

    internal sealed class ProjectTomlFormat
    {
        public string? Profile { get; set; }
    }

    internal sealed class ProjectTomlCanon
    {
        public string? Lang { get; set; }
        public string? OrgStyle { get; set; }
        public string? OrgStyleRoot { get; set; }
        public string? CanonFile { get; set; }
        public int? PreviewLines { get; set; }
        public int? BudgetPersonal { get; set; }
        public int? BudgetOrgLang { get; set; }
        public int? BudgetProject { get; set; }
        public string? OperatorPrefsRelpath { get; set; }
        public string? OrgLangFile { get; set; }
    }
}
