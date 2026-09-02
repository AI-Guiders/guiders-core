namespace DotNetWorkspace.Core;

public sealed class SolutionProjectGraphLoader : ISolutionProjectGraphLoader
{
    public SolutionProjectGraph Load(string solutionOrProjectPath) =>
        SolutionGraphParser.Parse(solutionOrProjectPath);

    public DotNetProjectEntry? TryResolveOwningProject(
        string filePath,
        string? solutionOrProjectPath = null,
        DotNetProjectKind? kindFilter = null)
    {
        var walkUp = ProjectOwnershipRules.WalkUpOwningProject(filePath, kindFilter);
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
            return walkUp;

        try
        {
            var graph = Load(solutionOrProjectPath);
            return graph.TryResolveOwningProject(filePath, kindFilter) ?? walkUp;
        }
        catch
        {
            return walkUp;
        }
    }

    public void Invalidate(string? solutionOrProjectPath = null)
    {
    }
}
