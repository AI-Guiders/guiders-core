namespace DotNetBuildTest.Core;

/// <summary>Сопоставляет путь с файлом для <c>dotnet build/test/publish</c> (.sln, .slnx, .slnf, .csproj, .fsproj, .vbproj).</summary>
public static class SolutionOrProjectPathResolver
{
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path.Trim());

        if (File.Exists(full))
        {
            if (IsSolutionOrProjectFile(full))
                return full;

            throw new ArgumentException($"Not a solution/project file (.sln, .slnx, .slnf, .csproj, .fsproj, .vbproj): {path}");
        }

        if (Directory.Exists(full))
        {
            var sln = Directory.GetFiles(full, "*.sln").OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (sln is not null)
                return sln;

            var slnx = Directory.GetFiles(full, "*.slnx").OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (slnx is not null)
                return slnx;

            var projects = Directory.EnumerateFiles(full, "*.csproj")
                .Concat(Directory.EnumerateFiles(full, "*.fsproj"))
                .Concat(Directory.EnumerateFiles(full, "*.vbproj"))
                .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (projects.Length == 1)
                return projects[0];
            if (projects.Length > 1)
                throw new ArgumentException(
                    $"Multiple project files in directory; specify a .sln/.slnx, a project file, or a folder with a single project: {full}");

            throw new ArgumentException($"No .sln, .slnx or project file found in directory: {full}");
        }

        throw new ArgumentException($"Path not found: {path}");
    }

    private static bool IsSolutionOrProjectFile(string full) =>
        full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);
}
