namespace DotNetBuildTest.Core;

public sealed record BuildTestExecutionRequest(
    string SolutionPath,
    bool WaitForCompletion,
    bool IncludeRawOutput,
    string Detail,
    int TimeoutSeconds,
    DotnetExecutionOptions DotnetOptions);
