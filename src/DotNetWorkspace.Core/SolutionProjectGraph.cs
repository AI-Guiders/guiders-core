namespace DotNetWorkspace.Core;

/// <summary>Structural IR: solution anchor + managed projects (.csproj / .fsproj).</summary>
public sealed record SolutionProjectGraph(
    string SolutionPath,
    string SolutionDirectory,
    IReadOnlyList<DotNetProjectEntry> Projects)
{
    public DotNetProjectEntry? TryResolveOwningProject(string filePath, DotNetProjectKind? kindFilter = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        string fullFile;
        try
        {
            fullFile = Path.GetFullPath(filePath.Trim());
        }
        catch
        {
            return null;
        }

        DotNetProjectEntry? best = null;
        var bestLen = -1;

        foreach (var project in Projects)
        {
            if (kindFilter is { } kind && project.Kind != kind)
                continue;

            if (!ProjectOwnershipRules.FileBelongsToProject(fullFile, project.AbsolutePath))
                continue;

            if (project.AbsolutePath.Length > bestLen)
            {
                best = project;
                bestLen = project.AbsolutePath.Length;
            }
        }

        return best;
    }
}
