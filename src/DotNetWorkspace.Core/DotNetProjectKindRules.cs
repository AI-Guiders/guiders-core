namespace DotNetWorkspace.Core;

public static class DotNetProjectKindRules
{
    public static DotNetProjectKind FromProjectPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DotNetProjectKind.Unknown;

        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return DotNetProjectKind.CSharp;

        if (path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
            return DotNetProjectKind.FSharp;

        return DotNetProjectKind.Unknown;
    }

    public static bool IsManagedProject(string? path) =>
        FromProjectPath(path) is DotNetProjectKind.CSharp or DotNetProjectKind.FSharp;
}
