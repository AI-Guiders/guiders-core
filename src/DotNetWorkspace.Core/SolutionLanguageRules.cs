namespace DotNetWorkspace.Core;

/// <summary>Language composition inferred from a solution/project graph anchor.</summary>
public enum SolutionLanguageComposition
{
    Unknown,
    CSharpOnly,
    FSharpOnly,
    Mixed,
}

/// <summary>Infer session language from .sln/.slnx project membership (ADR-0062 first-class rule).</summary>
public static class SolutionLanguageRules
{
    public static SolutionLanguageComposition InferComposition(SolutionProjectGraph graph)
    {
        var hasCSharp = false;
        var hasFSharp = false;

        foreach (var project in graph.Projects)
        {
            switch (project.Kind)
            {
                case DotNetProjectKind.CSharp:
                    hasCSharp = true;
                    break;
                case DotNetProjectKind.FSharp:
                    hasFSharp = true;
                    break;
            }

            if (hasCSharp && hasFSharp)
                return SolutionLanguageComposition.Mixed;
        }

        if (hasCSharp && hasFSharp)
            return SolutionLanguageComposition.Mixed;
        if (hasFSharp)
            return SolutionLanguageComposition.FSharpOnly;
        if (hasCSharp)
            return SolutionLanguageComposition.CSharpOnly;
        return SolutionLanguageComposition.Unknown;
    }

    public static SolutionLanguageComposition TryInferFromAnchor(string solutionOrProjectPath)
    {
        try
        {
            var graph = DotNetWorkspace.Load(solutionOrProjectPath);
            return InferComposition(graph);
        }
        catch
        {
            return SolutionLanguageComposition.Unknown;
        }
    }
}
