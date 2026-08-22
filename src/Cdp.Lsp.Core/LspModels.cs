using System.Text.Json.Serialization;

namespace Cdp.Lsp;

public sealed record LspPosition(int Line, int Character);

public sealed record LspRange(LspPosition Start, LspPosition End);

public sealed record LspLocation(string Uri, LspRange Range);

public sealed record LspTextEdit(LspRange Range, string NewText);

public sealed record LspWorkspaceEdit(
    IReadOnlyDictionary<string, IReadOnlyList<LspTextEdit>> Changes);

public sealed record LspDiagnostic(
    LspRange Range,
    string Severity,
    string? Code,
    string Message,
    string? Source);

public sealed record LspDocumentSymbol(
    string Name,
    string Kind,
    LspRange Range,
    LspRange SelectionRange,
    IReadOnlyList<LspDocumentSymbol>? Children);

public sealed record LspHoverInfo(string? Contents, LspRange? Range);

public sealed record LspCodeActionItem(
    int Index,
    string Title,
    string? Kind,
    bool HasEdit,
    bool NeedsResolve);

public sealed class LspServerCaps
{
    public bool Definition { get; init; }
    public bool References { get; init; }
    public bool DocumentSymbol { get; init; }
    public bool Hover { get; init; }
    public bool Rename { get; init; }
    public bool CodeAction { get; init; }
    public bool DiagnosticPull { get; init; }
}
