using DotNetWorkspace.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcp.ServiceLayer;

internal static class WorkspaceOpen
{
    public static async Task<Solution?> OpenSolutionOrProjectAsync(
        MSBuildWorkspace workspace,
        string solutionOrProjectPath,
        CancellationToken cancellationToken)
    {
        if (WorkspaceAnchorResolve.IsClassicSolutionFile(solutionOrProjectPath))
            return await workspace.OpenSolutionAsync(solutionOrProjectPath, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (WorkspaceAnchorResolve.IsMultiProjectAnchor(solutionOrProjectPath))
        {
            var projects = WorkspaceAnchorResolve.GetManagedProjects(solutionOrProjectPath);
            if (projects.Count == 0)
                throw new InvalidOperationException(
                    $".slnx/.slnf contains no loadable managed projects (.csproj, .fsproj): {solutionOrProjectPath}");

            Solution? solution = null;
            var opened = 0;
            foreach (var entry in projects)
            {
                if (entry.Kind != DotNetProjectKind.CSharp)
                    continue;

                if (ProjectAlreadyLoaded(workspace, entry.AbsolutePath))
                {
                    solution = workspace.CurrentSolution;
                    opened++;
                    continue;
                }

                try
                {
                    var project = await workspace.OpenProjectAsync(entry.AbsolutePath, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    solution = project.Solution;
                    opened++;
                }
                catch (Exception ex) when (IsAlreadyInWorkspace(ex))
                {
                    solution = workspace.CurrentSolution;
                    opened++;
                }
            }

            if (opened == 0)
                throw new InvalidOperationException(
                    $"MSBuildWorkspace could not open any project from anchor: {solutionOrProjectPath}");

            return solution;
        }

        var single = await workspace.OpenProjectAsync(solutionOrProjectPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return single.Solution;
    }

    static bool ProjectAlreadyLoaded(MSBuildWorkspace workspace, string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        return workspace.CurrentSolution.Projects.Any(p =>
            p.FilePath is { } fp
            && string.Equals(Path.GetFullPath(fp), full, StringComparison.OrdinalIgnoreCase));
    }

    static bool IsAlreadyInWorkspace(Exception ex) =>
        ex.Message.Contains("already part of the workspace", StringComparison.OrdinalIgnoreCase);
}
