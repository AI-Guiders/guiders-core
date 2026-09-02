namespace DotNetWorkspace.Core;

public interface ISolutionProjectGraphLoader
{
    SolutionProjectGraph Load(string solutionOrProjectPath);

    DotNetProjectEntry? TryResolveOwningProject(
        string filePath,
        string? solutionOrProjectPath = null,
        DotNetProjectKind? kindFilter = null);

    void Invalidate(string? solutionOrProjectPath = null);
}
