namespace DotNetWorkspace.Core;

public sealed record DotNetProjectEntry(
    string AbsolutePath,
    string RelativePath,
    string DisplayName,
    DotNetProjectKind Kind,
    string? SolutionFolder = null);
