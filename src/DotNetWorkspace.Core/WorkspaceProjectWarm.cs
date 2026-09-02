namespace DotNetWorkspace.Core;

/// <summary>Session warm: solution graph → per-project phased context.</summary>
public static class WorkspaceProjectWarm
{
    static ISdkProjectContextLoader _loader = new PhasedSdkProjectContextLoader();

    public static ISdkProjectContextLoader Loader
    {
        get => _loader;
        set => _loader = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static readonly ProjectContextLoadOptions FSharpWarmOptions =
        new(EnsureRestore: true, EnsureBuild: true, MinimumPhase: ProjectContextPhase.Compile);

    public static void WarmSolution(
        string solutionOrProjectPath,
        DotNetProjectKind? kindFilter = null,
        ProjectContextLoadOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
            return;

        var graph = DotNetWorkspace.Load(solutionOrProjectPath);
        foreach (var project in graph.Projects)
        {
            if (kindFilter is { } kind && project.Kind != kind)
                continue;

            var loadOptions = options
                ?? (project.Kind == DotNetProjectKind.FSharp ? FSharpWarmOptions : null);

            try
            {
                Loader.Warm(project.AbsolutePath, loadOptions);
            }
            catch
            {
                // Warm is best-effort; language tools retry on demand.
            }
        }
    }
}
