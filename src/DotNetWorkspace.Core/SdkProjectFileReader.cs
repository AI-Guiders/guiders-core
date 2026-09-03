using System.Xml.Linq;

namespace DotNetWorkspace.Core;

internal static class SdkProjectFileReader
{
    public sealed record ProjectFileModel(
        string ProjectPath,
        string TargetFramework,
        IReadOnlyList<string> SourceFiles,
        IReadOnlyList<string> DefineConstants);

    public static ProjectFileModel Read(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        var dir = Path.GetDirectoryName(full) ?? "";
        var doc = XDocument.Load(full);
        var root = doc.Root ?? throw new InvalidOperationException($"Empty project file '{full}'.");

        var tfm = ReadTargetFramework(root)
            ?? throw new InvalidOperationException($"No TargetFramework in '{full}'.");

        var sources = new List<string>();
        foreach (var itemGroup in root.Elements().Where(e => e.Name.LocalName == "ItemGroup"))
        {
            foreach (var item in itemGroup.Elements().Where(e => e.Name.LocalName is "Compile" or "None"))
            {
                var include = item.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                    continue;
                if (!include.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)
                    && !include.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var sourcePath = Path.GetFullPath(Path.Combine(dir, include.Replace('\\', Path.DirectorySeparatorChar)));
                if (!sources.Contains(sourcePath, StringComparer.OrdinalIgnoreCase))
                    sources.Add(sourcePath);
            }
        }

        var defines = ReadDefineConstants(root);

        return new ProjectFileModel(full, tfm, sources, defines);
    }

    static string? ReadTargetFramework(XElement root)
    {
        foreach (var pg in root.Elements().Where(e => e.Name.LocalName == "PropertyGroup"))
        {
            var tfm = pg.Elements().FirstOrDefault(e => e.Name.LocalName == "TargetFramework")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(tfm))
                return tfm;
        }

        return null;
    }

    static IReadOnlyList<string> ReadDefineConstants(XElement root)
    {
        var raw = root.Descendants()
            .Where(e => e.Name.LocalName == "DefineConstants")
            .Select(e => e.Value)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string ResolveAssemblyName(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        var doc = XDocument.Load(full);
        var root = doc.Root ?? throw new InvalidOperationException($"Empty project file '{full}'.");

        var explicitName = root.Descendants()
            .Where(e => e.Name.LocalName == "AssemblyName")
            .Select(e => e.Value.Trim())
            .FirstOrDefault(static v => !string.IsNullOrWhiteSpace(v));

        return !string.IsNullOrWhiteSpace(explicitName)
            ? explicitName
            : Path.GetFileNameWithoutExtension(full);
    }
}
