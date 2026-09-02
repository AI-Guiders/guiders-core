namespace DotNetWorkspace.Core;

/// <summary>Process-wide workspace facade: parse slnx/sln and resolve owning csproj/fsproj.</summary>
public static class DotNetWorkspace
{
    static ISolutionProjectGraphLoader _default =
        new CachingSolutionProjectGraphLoader(new SolutionProjectGraphLoader());

    public static ISolutionProjectGraphLoader Default
    {
        get => _default;
        set => _default = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static SolutionProjectGraph Load(string solutionOrProjectPath) =>
        Default.Load(solutionOrProjectPath);

    public static DotNetProjectEntry? TryResolveOwningProject(
        string filePath,
        string? solutionOrProjectPath = null,
        DotNetProjectKind? kindFilter = null) =>
        Default.TryResolveOwningProject(filePath, solutionOrProjectPath, kindFilter);
}
