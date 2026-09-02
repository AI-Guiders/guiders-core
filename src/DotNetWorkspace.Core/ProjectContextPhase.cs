namespace DotNetWorkspace.Core;

/// <summary>Phased project context materialization (workspace → restore → compile).</summary>
public enum ProjectContextPhase
{
    /// <summary>Project file parsed; graph membership known.</summary>
    ProjectFile = 1,

    /// <summary><c>obj/project.assets.json</c> available (restore done).</summary>
    Restored = 2,

    /// <summary><c>dotnet build</c> completed; generated sources under <c>obj/</c> available.</summary>
    Built = 3,

    /// <summary>Reference assemblies + sources resolved for language services.</summary>
    Compile = 4,
}
