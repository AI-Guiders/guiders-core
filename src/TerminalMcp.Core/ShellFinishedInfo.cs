namespace TerminalMcp.Core;

/// <summary>Raised when a shell tab finishes a command (foreground or background waiter).</summary>
public sealed record ShellFinishedInfo(
    string Tab,
    string Command,
    string Cwd,
    int ExitCode,
    bool Background,
    DateTimeOffset FinishedUtc);
