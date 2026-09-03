using System.Collections.Concurrent;

namespace DotNetWorkspace.Core;

public sealed class PhasedSdkProjectContextLoader : ISdkProjectContextLoader
{
    readonly ConcurrentDictionary<string, SdkProjectContext> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public SdkProjectContext Load(string projectPath, ProjectContextLoadOptions? options = null)
    {
        options ??= new ProjectContextLoadOptions();
        var key = CacheKey(projectPath, options);
        return _cache.GetOrAdd(key, _ => LoadFresh(projectPath, options));
    }

    public void Warm(string projectPath, ProjectContextLoadOptions? options = null) =>
        Load(projectPath, options);

    public void Invalidate(string? projectPath = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            _cache.Clear();
            return;
        }

        var prefix = Path.GetFullPath(projectPath.Trim()) + "|";
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            _cache.TryRemove(key, out _);
    }

    static string CacheKey(string projectPath, ProjectContextLoadOptions options) =>
        $"{Path.GetFullPath(projectPath.Trim())}|{options.CacheFingerprint}";

    static SdkProjectContext LoadFresh(string projectPath, ProjectContextLoadOptions options)
    {
        if (!DotNetProjectKindRules.IsManagedProject(projectPath))
            throw new NotSupportedException($"Not a managed project file: '{projectPath}'.");

        var projectFile = SdkProjectFileReader.Read(projectPath);
        var projectDir = Path.GetDirectoryName(projectFile.ProjectPath) ?? "";
        var kind = DotNetProjectKindRules.FromProjectPath(projectFile.ProjectPath);

        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
        var phase = ProjectContextPhase.ProjectFile;

        if (options.EnsureRestore && !File.Exists(assetsPath))
        {
            var exit = DotNetCli.Restore(projectFile.ProjectPath);
            if (exit != 0)
                throw new InvalidOperationException($"dotnet restore failed ({exit}) for '{projectFile.ProjectPath}'.");
        }

        if (File.Exists(assetsPath))
            phase = ProjectContextPhase.Restored;

        if (options.EnsureBuild && kind == DotNetProjectKind.FSharp)
        {
            if (DotNetCli.Build(projectFile.ProjectPath) == 0)
                phase = ProjectContextPhase.Built;
        }

        var references = ProjectAssetsReader.ReadReferenceAssemblies(projectFile.ProjectPath, projectFile.TargetFramework);
        if (references.Count > 0)
            phase = ProjectContextPhase.Compile;

        if ((int)phase < (int)options.MinimumPhase)
        {
            throw new InvalidOperationException(
                $"Project context for '{projectFile.ProjectPath}' reached phase {phase}, required {options.MinimumPhase}.");
        }

        var sources = ResolveSourceFiles(projectFile, projectDir, phase);

        return new SdkProjectContext(
            projectFile.ProjectPath,
            projectDir,
            projectFile.TargetFramework,
            phase,
            sources,
            projectFile.DefineConstants,
            references);
    }

    static IReadOnlyList<string> ResolveSourceFiles(
        SdkProjectFileReader.ProjectFileModel projectFile,
        string projectDir,
        ProjectContextPhase phase)
    {
        var sources = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void add(string path)
        {
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                sources.Add(full);
        }

        foreach (var path in projectFile.SourceFiles)
            add(path);

        if (sources.Count == 0)
        {
            foreach (var path in Directory.EnumerateFiles(projectDir, "*.fs", SearchOption.TopDirectoryOnly))
                add(path);
        }

        if ((int)phase >= (int)ProjectContextPhase.Built)
        {
            var objDir = Path.Combine(projectDir, "obj");
            if (Directory.Exists(objDir))
            {
                foreach (var path in Directory.EnumerateFiles(objDir, "*.fs", SearchOption.AllDirectories))
                    add(path);
            }
        }

        return sources;
    }
}
