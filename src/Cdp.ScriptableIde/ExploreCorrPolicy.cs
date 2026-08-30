#nullable enable
using AIGuiders.Platform.Configurations.Workspace;

namespace Cdp.ScriptableIde;

/// <summary>CDP façade over platform <see cref="WorkspaceExploreCorrPolicy"/>.</summary>
public static class ExploreCorrPolicy
{
    public enum Mode
    {
        Full,
        Card,
        Off,
    }

    public static Mode ResolveMode(string absoluteFilePath, string? workspaceRootHint = null)
    {
        if (string.IsNullOrWhiteSpace(absoluteFilePath))
            return Mode.Full;

        string abs;
        try { abs = Path.GetFullPath(absoluteFilePath.Trim()); }
        catch { return Mode.Full; }

        var root = WorkspaceCorrespondence.FindWorkspaceRoot(abs, workspaceRootHint);
        if (root is null)
            return Mode.Full;

        return Map(WorkspaceSources.ResolveExploreCorrMode(abs, root));
    }

    static Mode Map(WorkspaceExploreCorrPolicy.Mode mode) => mode switch
    {
        WorkspaceExploreCorrPolicy.Mode.Off => Mode.Off,
        WorkspaceExploreCorrPolicy.Mode.Card => Mode.Card,
        _ => Mode.Full,
    };
}
