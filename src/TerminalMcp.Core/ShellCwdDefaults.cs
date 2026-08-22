namespace TerminalMcp.Core;

/// <summary>Optional cwd fallbacks (CDP session maps ProjectRoot/ScmRoot here).</summary>
public sealed class ShellCwdDefaults
{
    public string? ProjectRoot { get; init; }
    public string? ScmRoot { get; init; }

    public static ShellCwdDefaults Empty { get; } = new();
}
