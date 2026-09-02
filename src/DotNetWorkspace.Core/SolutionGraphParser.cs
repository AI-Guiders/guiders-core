using System.Xml.Linq;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace DotNetWorkspace.Core;

internal static class SolutionGraphParser
{
    public static SolutionProjectGraph Parse(string solutionOrProjectPath)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
            throw new ArgumentException("Solution or project path is required.", nameof(solutionOrProjectPath));

        var normalized = Path.GetFullPath(solutionOrProjectPath.Trim());
        if (!File.Exists(normalized))
            throw new FileNotFoundException($"Solution or project file not found: '{normalized}'", normalized);

        var solutionDir = Path.GetDirectoryName(normalized) ?? "";
        var ext = Path.GetExtension(normalized);

        IReadOnlyList<string> projectPaths = ext.ToLowerInvariant() switch
        {
            ".csproj" or ".fsproj" => [normalized],
            ".slnx" or ".sln" or ".slnf" => LoadSolutionProjects(normalized, solutionDir, ext),
            _ => throw new NotSupportedException(
                $"Unsupported solution anchor '{ext}'. Expected .slnx, .sln, .csproj, or .fsproj."),
        };

        var entries = projectPaths
            .Where(File.Exists)
            .Select(path => ToEntry(path, solutionDir))
            .DistinctBy(entry => entry.AbsolutePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SolutionProjectGraph(normalized, solutionDir, entries);
    }

    public static DotNetProjectEntry ToEntry(string absoluteProjectPath, string solutionDirectory)
    {
        var full = Path.GetFullPath(absoluteProjectPath);
        string relative;
        try
        {
            relative = Path.GetRelativePath(solutionDirectory, full);
        }
        catch
        {
            relative = Path.GetFileName(full);
        }

        return new DotNetProjectEntry(
            full,
            relative,
            Path.GetFileNameWithoutExtension(full),
            DotNetProjectKindRules.FromProjectPath(full));
    }

    static IReadOnlyList<string> LoadSolutionProjects(string solutionPath, string solutionDir, string ext)
    {
        if (TryLoadWithSolutionPersistence(solutionPath, solutionDir, out var fromPersistence))
            return fromPersistence;

        return ext.ToLowerInvariant() switch
        {
            ".slnx" => LoadSlnxXml(solutionPath, solutionDir),
            ".sln" => LoadClassicSln(solutionPath, solutionDir),
            _ => [],
        };
    }

    static bool TryLoadWithSolutionPersistence(string solutionPath, string solutionDir, out IReadOnlyList<string> paths)
    {
        paths = [];
        try
        {
            var serializer = SolutionSerializers.GetSerializerByMoniker(solutionPath);
            if (serializer is null)
                return false;

            SolutionModel model = serializer.OpenAsync(solutionPath, CancellationToken.None).GetAwaiter().GetResult();
            paths = model.SolutionProjects
                .Select(project => project.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(Path.Combine(solutionDir, path!.Replace('/', Path.DirectorySeparatorChar))))
                .Where(DotNetProjectKindRules.IsManagedProject)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return paths.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    static List<string> LoadSlnxXml(string solutionPath, string solutionDir)
    {
        var results = new List<string>();
        using var stream = File.OpenRead(solutionPath);
        var doc = XDocument.Load(stream);
        var root = doc.Root;
        if (root is null)
            return results;

        void Visit(XElement container)
        {
            foreach (var child in container.Elements())
            {
                if (child.Name.LocalName == "Folder")
                {
                    Visit(child);
                    continue;
                }

                if (child.Name.LocalName != "Project")
                    continue;

                var path = (string?)child.Attribute("Path");
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var full = Path.GetFullPath(Path.Combine(solutionDir, path.Replace('/', Path.DirectorySeparatorChar)));
                if (DotNetProjectKindRules.IsManagedProject(full))
                    results.Add(full);
            }
        }

        Visit(root);
        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static List<string> LoadClassicSln(string solutionPath, string solutionDir)
    {
        var results = new List<string>();
        foreach (var line in File.ReadAllLines(solutionPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Project(", StringComparison.Ordinal))
                continue;

            var path = ExtractPathFromSlnProjectLine(trimmed);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var full = Path.GetFullPath(Path.Combine(solutionDir, path.Replace('/', Path.DirectorySeparatorChar)));
            if (DotNetProjectKindRules.IsManagedProject(full))
                results.Add(full);
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string? ExtractPathFromSlnProjectLine(string line)
    {
        var firstQuote = line.IndexOf('"');
        if (firstQuote < 0)
            return null;

        var secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0)
            return null;

        var thirdQuote = line.IndexOf('"', secondQuote + 1);
        if (thirdQuote < 0)
            return null;

        var fourthQuote = line.IndexOf('"', thirdQuote + 1);
        if (fourthQuote < 0)
            return null;

        return line.Substring(thirdQuote + 1, fourthQuote - thirdQuote - 1);
    }
}
