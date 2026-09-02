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

        var sources = root.Descendants()
            .Where(e => e.Name.LocalName is "Compile" or "None" && e.Attribute("Include") is not null)
            .Select(e => e.Attribute("Include")!.Value)
            .Where(static p => p.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFullPath(Path.Combine(dir, p.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
}
