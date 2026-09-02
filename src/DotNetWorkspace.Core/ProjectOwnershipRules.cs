namespace DotNetWorkspace.Core;

internal static class ProjectOwnershipRules
{
    public static bool FileBelongsToProject(string filePath, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(projectPath))
            return false;

        string fullFile;
        string projectDir;
        try
        {
            fullFile = Path.GetFullPath(filePath);
            projectDir = Path.GetFullPath(Path.GetDirectoryName(projectPath) ?? "");
        }
        catch
        {
            return false;
        }

        if (projectDir.Length == 0)
            return false;

        var prefix = projectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static DotNetProjectEntry? WalkUpOwningProject(string filePath, DotNetProjectKind? kindFilter = null)
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

        var dir = Path.GetDirectoryName(fullFile);
        while (!string.IsNullOrEmpty(dir))
        {
            string[] patterns = kindFilter switch
            {
                DotNetProjectKind.CSharp => ["*.csproj"],
                DotNetProjectKind.FSharp => ["*.fsproj"],
                _ => ["*.csproj", "*.fsproj"],
            };

            foreach (var pattern in patterns)
            {
                string[] hits;
                try
                {
                    hits = Directory.GetFiles(dir, pattern);
                }
                catch
                {
                    continue;
                }

                if (hits.Length == 0)
                    continue;

                var pick = hits.Length == 1
                    ? hits[0]
                    : hits.FirstOrDefault(p =>
                          string.Equals(
                              Path.GetFileNameWithoutExtension(p),
                              Path.GetFileName(dir),
                              StringComparison.OrdinalIgnoreCase))
                      ?? hits[0];

                return SolutionGraphParser.ToEntry(pick, dir);
            }

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;

            dir = parent;
        }

        return null;
    }
}
