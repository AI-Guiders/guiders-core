using System.Text.Json;

namespace DotNetWorkspace.Core;

internal static class ProjectAssetsReader
{
    public static IReadOnlyList<string> ReadReferenceAssemblies(string projectPath, string targetFramework)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            return [];

        using var stream = File.OpenRead(assetsPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectCompileReferences(root, projectPath, targetFramework, refs, visitedProjects);
        foreach (var path in FrameworkRefPackResolver.ResolveRefAssemblies(targetFramework))
            refs.Add(path);

        return refs.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void CollectCompileReferences(
        JsonElement root,
        string projectPath,
        string targetFramework,
        HashSet<string> refs,
        HashSet<string> visitedProjects)
    {
        if (!root.TryGetProperty("targets", out var targets)
            || !targets.TryGetProperty(targetFramework, out var tfmTargets)
            || !root.TryGetProperty("libraries", out var libraries))
            return;

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
        var packagesRoot = ResolveNuGetPackagesRoot();

        foreach (var target in tfmTargets.EnumerateObject())
        {
            var libKey = target.Name;
            if (!libraries.TryGetProperty(libKey, out var library))
                continue;

            var libType = library.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (libType == "project")
            {
                if (library.TryGetProperty("msbuildProject", out var msbuildProject)
                    && !string.IsNullOrWhiteSpace(msbuildProject.GetString()))
                {
                    var refProjectPath = Path.GetFullPath(
                        Path.Combine(projectDir, msbuildProject.GetString()!.Replace('/', Path.DirectorySeparatorChar)));
                    CollectProjectReferenceAssembly(refProjectPath, targetFramework, refs, visitedProjects);
                }

                continue;
            }

            if (!library.TryGetProperty("path", out var pathEl))
                continue;

            var packageDir = Path.Combine(packagesRoot, pathEl.GetString()!.Replace('/', Path.DirectorySeparatorChar));
            CollectCompileDlls(target.Value, packageDir, refs);
        }
    }

    static void CollectProjectReferenceAssembly(
        string refProjectPath,
        string targetFramework,
        HashSet<string> refs,
        HashSet<string> visitedProjects)
    {
        if (!File.Exists(refProjectPath) || !visitedProjects.Add(Path.GetFullPath(refProjectPath)))
            return;

        if (TryAddBuiltOutput(refProjectPath, targetFramework, refs))
        {
            var refAssets = Path.Combine(Path.GetDirectoryName(refProjectPath) ?? "", "obj", "project.assets.json");
            if (File.Exists(refAssets))
            {
                using var stream = File.OpenRead(refAssets);
                using var doc = JsonDocument.Parse(stream);
                CollectCompileReferences(doc.RootElement, refProjectPath, targetFramework, refs, visitedProjects);
            }
        }
    }

    static bool TryAddBuiltOutput(string refProjectPath, string targetFramework, HashSet<string> refs)
    {
        var refDir = Path.GetDirectoryName(refProjectPath) ?? "";
        var assemblyName = SdkProjectFileReader.ResolveAssemblyName(refProjectPath);

        foreach (var config in new[] { "Debug", "Release" })
        {
            var dll = Path.Combine(refDir, "bin", config, targetFramework, assemblyName + ".dll");
            if (!File.Exists(dll))
                continue;

            refs.Add(dll);
            return true;
        }

        return false;
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
