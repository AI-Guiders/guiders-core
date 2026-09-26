using DotNetWorkspace.Core;

namespace RoslynMcp.ServiceLayer;

/// <summary>
/// Tactical bridge: session anchor string → dotnet project files for <see cref="MSBuildWorkspace"/>.
/// Graph parse SSOT is <see cref="DotNetWorkspace"/> (GUIDERS-ADR-0062 §6 — port, not lifecycle).
/// Roslyn host must not parse .slnx itself or treat the anchor as a single MSBuild project file.
/// </summary>
internal static class WorkspaceAnchorResolve
{
    public static bool IsClassicSolutionFile(string path) =>
        string.Equals(Path.GetExtension(path), ".sln", StringComparison.OrdinalIgnoreCase);

    public static bool IsMultiProjectAnchor(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".slnx", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ext, ".slnf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryLoadGraph(string solutionOrProjectPath, out SolutionProjectGraph graph)
    {
        graph = null!;
        try
        {
            graph = global::DotNetWorkspace.Core.DotNetWorkspace.Load(solutionOrProjectPath);
            return graph.Projects.Count > 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Managed MSBuild projects from graph (.csproj, .fsproj, … per <see cref="DotNetProjectKindRules"/>).
    /// </summary>
    public static IReadOnlyList<DotNetProjectEntry> GetManagedProjects(string solutionOrProjectPath)
    {
        if (!TryLoadGraph(solutionOrProjectPath, out var graph))
            return [];

        return graph.Projects
            .Where(static p => File.Exists(p.AbsolutePath))
            .ToList();
    }
}