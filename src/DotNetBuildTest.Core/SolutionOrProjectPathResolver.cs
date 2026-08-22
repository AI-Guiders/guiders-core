namespace DotNetBuildTest.Core;

/// <summary>Сопоставляет путь с файлом для <c>dotnet build/test/publish</c> (.sln, .slnx, .slnf, .csproj).</summary>
public static class SolutionOrProjectPathResolver
{
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path.Trim());

        if (File.Exists(full))
        {
            if (IsSolutionOrProjectFile(full))
                return full;

            throw new ArgumentException($"Not a solution/project file (.sln, .slnx, .slnf, .csproj): {path}");
        }

        if (Directory.Exists(full))
        {
            var sln = Directory.GetFiles(full, "*.sln").OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (sln is not null)
                return sln;

            var slnx = Directory.GetFiles(full, "*.slnx").OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (slnx is not null)
                return slnx;

            var csprojs = Directory.GetFiles(full, "*.csproj").OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            if (csprojs.Length == 1)
                return csprojs[0];
            if (csprojs.Length > 1)
                throw new ArgumentException(
                    $"Multiple .csproj in directory; specify a .sln/.slnx, a .csproj, or a folder with a single project: {full}");

            throw new ArgumentException($"No .sln, .slnx or .csproj found in directory: {full}");
        }

        throw new ArgumentException($"Path not found: {path}");
    }

    private static bool IsSolutionOrProjectFile(string full) =>
        full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase) ||
        full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
}
