namespace DotNetWorkspace.Core;

public sealed record SdkProjectContext(
    string ProjectPath,
    string ProjectDirectory,
    string TargetFramework,
    ProjectContextPhase Phase,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> DefineConstants,
    IReadOnlyList<string> ReferenceAssemblies);
