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
                try
                {
                    var project = await workspace.OpenProjectAsync(entry.AbsolutePath, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    solution = project.Solution;
                    opened++;
                }
                catch (Exception ex) when (entry.Kind == DotNetProjectKind.FSharp)
                {
                    // Roslyn MSBuild host is C#-centric; F# design-time is FCS (ADR-0061). Skip failed F# load.
                    _ = ex;
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
}
