using System.Text;
using System.Text.Json;

namespace DotnetDebug.Core;

/// <summary>Параметры раскрытия кадра (общий контракт MCP + IDE).</summary>
public sealed record DapFrameInspectionOptions
{
    public int FrameIndex { get; init; }
    public bool Fast { get; init; }
    public int? MaxDepth { get; init; }
    public int? MaxChildrenPerNode { get; init; }
    public int? TimeBudgetMs { get; init; }
    public bool FormatJson { get; init; }
    public bool JsonIndented { get; init; } = true;

    public int ResolveMaxDepth() =>
        MaxDepth ?? (Fast ? 0 : DapVariableExpansion.DefaultMaxDepth);

    public int ResolveMaxChildren() =>
        MaxChildrenPerNode ?? (Fast ? 24 : DapVariableExpansion.DefaultMaxChildrenPerNode);

    public int ResolveTimeBudgetMs() =>
        TimeBudgetMs ?? (Fast ? 700 : 1800);
}

/// <summary>Метаданные остановки для stop-context (host передаёт из своей сессии).</summary>
public sealed record DapStopContextMeta(
    int ThreadId,
    string? WorkspacePath = null,
    string? TargetPath = null,
    string? ExceptionText = null);

/// <summary>
/// Форматирование стека/переменных после DAP stopped — общее для MCP и in-proc IDE.
/// </summary>
public static class DapFrameInspection
{
    /// <summary>Markdown стека для потока.</summary>
    public static async Task<string> FormatStackTraceMarkdownAsync(
        DapClient client,
        int threadId,
        CancellationToken cancellationToken = default)
    {
        JsonElement? body;
        try
        {
            body = await DapShared.WithRetryAsync(() => client.StackTraceAsync(threadId, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return "# " + ex.Message;
        }

        if (body == null || !body.Value.TryGetProperty("stackFrames", out var frames))
            return "# No stack frames.";

        var sb = new StringBuilder();
        sb.AppendLine("# Stack trace");
        var i = 0;
        foreach (var f in frames.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = f.TryGetProperty("name", out var n) ? n.GetString() : "?";
            var line = f.TryGetProperty("line", out var ln) ? ln.GetInt32() : 0;
            var path = "";
            if (f.TryGetProperty("source", out var src) && src.TryGetProperty("path", out var p))
                path = p.GetString() ?? "";
            var id = f.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            sb.AppendLine($"  [{i}] {name} — {path}:{line} (id={id})");
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Markdown или JSON переменных кадра (scopes → expansion).</summary>
    public static async Task<string> FormatVariablesAsync(
        DapClient client,
        int threadId,
        DapFrameInspectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var frameIndex = options.FrameIndex;
        var fast = options.Fast;
        var maxDepth = options.ResolveMaxDepth();
        var maxChildren = options.ResolveMaxChildren();
        var timeBudgetMs = options.ResolveTimeBudgetMs();
        var formatJson = options.FormatJson;
        var jsonIndented = options.JsonIndented;

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(TimeSpan.FromMilliseconds(timeBudgetMs));
        var ct = budgetCts.Token;

        JsonElement? stackBody;
        try
        {
            stackBody = await DapShared.WithRetryAsync(() => client.StackTraceAsync(threadId, cancellationToken: ct))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return "# " + ex.Message;
        }

        if (stackBody == null || !stackBody.Value.TryGetProperty("stackFrames", out var frames))
            return "# No stack; run stack_trace first or ensure stopped.";

        var frameList = frames.EnumerateArray().ToList();
        if (frameIndex < 0 || frameIndex >= frameList.Count)
            return $"# frame_index {frameIndex} out of range (0..{frameList.Count - 1}).";

        var frame = frameList[frameIndex];
        if (!frame.TryGetProperty("id", out var idEl))
            return "# Frame has no id.";

        var frameId = idEl.GetInt32();
        var scopeBlocks = new List<(string Name, JsonElement Variables)>();
        var usedScopes = false;
        try
        {
            var scopesBody = await DapShared.WithRetryAsync(() => client.ScopesAsync(frameId, ct)).ConfigureAwait(false);
            if (scopesBody != null && scopesBody.Value.TryGetProperty("scopes", out var scopesArr))
            {
                foreach (var scope in scopesArr.EnumerateArray())
                {
                    if (!scope.TryGetProperty("variablesReference", out var vrefEl) || !vrefEl.TryGetInt32(out var vref) || vref == 0)
                        continue;
                    var scopeName = scope.TryGetProperty("name", out var sn) ? sn.GetString() : "?";
                    var varsBody = await DapShared.WithRetryAsync(() => client.VariablesAsync(vref, cancellationToken: ct))
                        .ConfigureAwait(false);
                    if (varsBody == null || !varsBody.Value.TryGetProperty("variables", out var vars))
                        continue;
                    usedScopes = true;
                    scopeBlocks.Add((scopeName ?? "?", vars));
                }
            }
        }
        catch (InvalidOperationException)
        {
            // scopes не поддерживается — ниже direct variables
        }

        if (!usedScopes)
        {
            JsonElement? varsBody;
            try
            {
                varsBody = await DapShared.WithRetryAsync(() => client.VariablesAsync(frameId, cancellationToken: ct))
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return "# " + ex.Message;
            }

            if (varsBody == null || !varsBody.Value.TryGetProperty("variables", out var vars))
                return "# No variables for this frame (tried scopes and direct variables).";
            scopeBlocks.Add(("variables", vars));
        }

        if (scopeBlocks.Count == 0)
            return "# No variable scopes for this frame.";

        var partial = false;
        string? partialNote = null;

        if (formatJson)
        {
            var built = new List<(string ScopeName, IReadOnlyList<DapVariableTreeNode> Roots)>(scopeBlocks.Count);
            foreach (var (name, varEl) in scopeBlocks)
            {
                try
                {
                    var tree = await DapVariableExpansion
                        .BuildExpandedTreeAsync(client, varEl, maxDepth, maxChildren, ct)
                        .ConfigureAwait(false);
                    built.Add((name, tree));
                }
                catch (OperationCanceledException)
                {
                    partial = true;
                    partialNote =
                        $"Stopped by time budget ({timeBudgetMs} ms). Use fast=true, lower max_depth/max_children_per_node, or inspect via variable children.";
                    break;
                }
            }

            return DapVariableExpansion.SerializeFrameVariablesDocumentToJson(
                frameIndex,
                maxDepth,
                maxChildren,
                built,
                partial,
                partialNote,
                jsonIndented);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Variables (frame {frameIndex})");
        sb.AppendLine(
            $"# (max_depth={maxDepth}, max_children_per_node={maxChildren}, time_budget_ms={timeBudgetMs}, fast={fast.ToString().ToLowerInvariant()})");
        foreach (var (name, varEl) in scopeBlocks)
        {
            sb.AppendLine($"## {name}");
            try
            {
                await DapVariableExpansion
                    .AppendExpandedVariablesAsync(client, sb, varEl, indent: "  ", depth: 0, maxDepth, maxChildren, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                sb.AppendLine($"# Partial: stopped by time budget ({timeBudgetMs} ms).");
                sb.AppendLine(
                    "# Tip: use fast=true, lower max_depth/max_children_per_node, and expand specific refs via variable children.");
                break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Stop-context: один снимок после stopped (stack + variables). Для MCP tool и CIDE in-proc.
/// </summary>
public static class DapStopContext
{
    /// <summary>Markdown: meta + stack + variables.</summary>
    public static async Task<string> FormatMarkdownAsync(
        DapClient client,
        DapStopContextMeta meta,
        DapFrameInspectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DapFrameInspectionOptions();
        var sb = new StringBuilder();
        sb.AppendLine("# Stop context");
        sb.AppendLine($"# threadId={meta.ThreadId}");
        if (meta.WorkspacePath is { } ws)
            sb.AppendLine($"# workspace={ws}");
        if (meta.TargetPath is { } tp)
            sb.AppendLine($"# target={tp}");
        if (meta.ExceptionText is { } ex)
            sb.AppendLine($"# exception={ex}");
        sb.AppendLine();

        var stack = await DapFrameInspection.FormatStackTraceMarkdownAsync(client, meta.ThreadId, cancellationToken)
            .ConfigureAwait(false);
        sb.AppendLine(stack.TrimEnd());
        sb.AppendLine();

        var vars = await DapFrameInspection.FormatVariablesAsync(client, meta.ThreadId, options, cancellationToken)
            .ConfigureAwait(false);
        sb.Append(vars.TrimEnd());
        sb.AppendLine();
        return sb.ToString();
    }
}
