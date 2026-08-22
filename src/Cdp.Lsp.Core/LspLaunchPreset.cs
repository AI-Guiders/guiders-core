namespace Cdp.Lsp;

/// <summary>How to spawn a language server for a CDP language id.</summary>
public sealed class LspLaunchPreset
{
    public required string Id { get; init; }
    public required string Command { get; init; }
    /// <summary>Tried in order when resolving PATH (e.g. basedpyright then pyright).</summary>
    public IReadOnlyList<string> CommandCandidates { get; init; } = [];
    public IReadOnlyList<string> Args { get; init; } = [];
    public IReadOnlyList<string> LanguageIds { get; init; } = [];
    public IReadOnlyList<string> RootMarkers { get; init; } = [];

    public static LspLaunchPreset DefaultPython { get; } = new()
    {
        Id = "python",
        // Open pyright codeAction ≈ createTypeStub only; basedpyright adds auto-import / ignore / etc.
        Command = "basedpyright-langserver",
        CommandCandidates = ["basedpyright-langserver", "pyright-langserver"],
        Args = ["--stdio"],
        LanguageIds = ["python"],
        RootMarkers = ["pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "pyrightconfig.json", ".git"]
    };

    public static IReadOnlyList<LspLaunchPreset> BuiltInDefaults { get; } =
    [
        DefaultPython
    ];
}
