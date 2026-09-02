namespace DotNetWorkspace.Core;

public sealed record ProjectContextLoadOptions(
    bool EnsureRestore = true,
    bool EnsureBuild = false,
    ProjectContextPhase MinimumPhase = ProjectContextPhase.Compile)
{
    internal string CacheFingerprint =>
        $"r:{EnsureRestore}|b:{EnsureBuild}|m:{(int)MinimumPhase}";
}
