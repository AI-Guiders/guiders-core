namespace DotNetWorkspace.Core;

internal static class FrameworkRefPackResolver
{
    public static IReadOnlyList<string> ResolveRefAssemblies(string targetFramework)
    {
        var packsRoot = Path.Combine(ResolveDotNetRoot(), "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packsRoot))
            return [];

        var refDir = ResolveRefDirectory(packsRoot, targetFramework);
        if (refDir is null)
            return [];

        return Directory.EnumerateFiles(refDir, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string? ResolveRefDirectory(string packsRoot, string targetFramework)
    {
        string? best = null;
        Version? bestVersion = null;

        foreach (var versionDir in Directory.EnumerateDirectories(packsRoot))
        {
            var refDir = Path.Combine(versionDir, "ref", targetFramework);
            if (!Directory.Exists(refDir))
                continue;

            if (!TryParsePackVersion(Path.GetFileName(versionDir), out var version))
                continue;

            if (bestVersion is null || version > bestVersion)
            {
                bestVersion = version;
                best = refDir;
            }
        }

        return best;
    }

    static bool TryParsePackVersion(string folderName, out Version version)
    {
        var core = folderName.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        return Version.TryParse(core, out version);
    }

    static string ResolveDotNetRoot()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
    }
}
