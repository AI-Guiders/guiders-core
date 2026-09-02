using System.Text.Json;

namespace DotNetWorkspace.Core;

internal static class ProjectAssetsReader
{
    public static IReadOnlyList<string> ReadReferenceAssemblies(string projectPath, string targetFramework)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "", "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            return [];

        using var stream = File.OpenRead(assetsPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectPackageCompilePaths(root, targetFramework, refs);
        foreach (var path in FrameworkRefPackResolver.ResolveRefAssemblies(targetFramework))
            refs.Add(path);

        return refs.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void CollectPackageCompilePaths(JsonElement root, string targetFramework, HashSet<string> refs)
    {
        if (!root.TryGetProperty("targets", out var targets)
            || !targets.TryGetProperty(targetFramework, out var tfmTargets))
            return;

        if (!root.TryGetProperty("libraries", out var libraries))
            return;

        var packagesRoot = ResolveNuGetPackagesRoot();

        foreach (var target in tfmTargets.EnumerateObject())
        {
            var libKey = target.Name;
            if (!libraries.TryGetProperty(libKey, out var library))
                continue;

            if (!library.TryGetProperty("path", out var pathEl))
                continue;

            var packageDir = Path.Combine(packagesRoot, pathEl.GetString()!.Replace('/', Path.DirectorySeparatorChar));
            CollectCompileDlls(target.Value, packageDir, refs);
        }
    }

    static void CollectCompileDlls(JsonElement targetNode, string packageDir, HashSet<string> refs)
    {
        if (!targetNode.TryGetProperty("compile", out var compile))
            return;

        foreach (var compilePath in compile.EnumerateObject())
        {
            var full = Path.GetFullPath(Path.Combine(packageDir, compilePath.Name.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(full))
                refs.Add(full);
        }
    }

    static string ResolveNuGetPackagesRoot()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".nuget", "packages");
    }
}
