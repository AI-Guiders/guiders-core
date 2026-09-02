namespace DotNetWorkspace.Core;

internal static class FrameworkRefPackResolver
{
    public static IReadOnlyList<string> ResolveRefAssemblies(string targetFramework)
    {
        var packsRoot = Path.Combine(ResolveDotNetRoot(), "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packsRoot))
            return [];

        var versionDir = Directory.EnumerateDirectories(packsRoot)
            .OrderByDescending(static p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (versionDir is null)
            return [];

        var refDir = Path.Combine(versionDir, "ref", targetFramework);
        if (!Directory.Exists(refDir))
            return [];

        return Directory.EnumerateFiles(refDir, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string ResolveDotNetRoot()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
    }
}
