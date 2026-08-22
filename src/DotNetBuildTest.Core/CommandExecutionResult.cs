namespace DotNetBuildTest.Core;

public sealed record CommandExecutionResult(
    int ExitCode,
    string Output,
    bool TimedOut,
    bool Cancelled,
    string? FailureReason);
