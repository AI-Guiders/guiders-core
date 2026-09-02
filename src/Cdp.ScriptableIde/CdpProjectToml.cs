using System.Text;
using AIGuiders.Platform.Execution.Configurations.Project;

namespace Cdp.ScriptableIde;

internal static class CdpProjectToml
{
    internal const string EmbeddedDefaultsResource = "cdp-project.defaults.toml";

    internal static ProjectDocument DeserializeEmbedded() =>
        ProjectSources.FromText(
            ReadEmbeddedRequired(EmbeddedDefaultsResource),
            EmbeddedDefaultsResource).Load();

    internal static ProjectDocument? TryDeserializeFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            return ProjectSources.FromFile(path).Load();
        }
        catch
        {
            return null;
        }
    }

    internal static ProjectDocument Merge(ProjectDocument embedded, ProjectDocument? overlay) =>
        ProjectSources.MergeDocuments(embedded, overlay);

    private static string ReadEmbeddedRequired(string resourceRelativePath)
    {
        if (!BundledScriptableIdeContent.TryReadEmbeddedText(resourceRelativePath, out var text))
            throw new InvalidOperationException($"Embedded TOML is missing: {resourceRelativePath}");
        return text;
    }
}
