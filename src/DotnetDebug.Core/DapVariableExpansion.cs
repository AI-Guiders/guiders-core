using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetDebug.Core;

/// <summary>
/// Рекурсивное раскрытие DAP variables по <c>variablesReference</c>, чтобы агент и панель отладки
/// видели элементы массивов и поля объектов, а не только сводку вида <c>{string[n]}</c>.
/// </summary>
public static class DapVariableExpansion
{
    public const int DefaultMaxDepth = 4;
    public const int DefaultMaxChildrenPerNode = 48;

    static readonly JsonSerializerOptions JsonDumpOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions JsonDumpCompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Рекурсивное дерево переменных для JSON (MCP: <c>format=json</c>).</summary>
    public static async Task<List<DapVariableTreeNode>> BuildExpandedTreeAsync(
        DapClient client,
        JsonElement variables,
        int maxDepth,
        int maxChildrenPerNode,
        CancellationToken cancellationToken) =>
        await BuildTreeLevelAsync(client, variables, 0, maxDepth, maxChildrenPerNode, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Документ по кадру: scopes → variables (деревья). Используй с аргументами <see cref="BuildExpandedTreeAsync"/> по каждому scope.
    /// </summary>
    public static string SerializeFrameVariablesDocumentToJson(
        int frameIndex,
        int maxDepth,
        int maxChildrenPerNode,
        IReadOnlyList<(string ScopeName, IReadOnlyList<DapVariableTreeNode> Roots)> scopes,
        bool partial,
        string? note,
        bool writeIndented)
    {
        var options = writeIndented ? JsonDumpOptions : JsonDumpCompactOptions;
        var scopeDtos = scopes.Select(static s => new ScopeJsonDto(s.ScopeName, s.Roots)).ToArray();
        return JsonSerializer.Serialize(
            new FrameVariablesJsonDto(frameIndex, maxDepth, maxChildrenPerNode, partial, note, scopeDtos),
            options);
    }

    sealed record FrameVariablesJsonDto(
        int FrameIndex,
        int MaxDepth,
        int MaxChildrenPerNode,
        bool Partial,
        string? Note,
        IReadOnlyList<ScopeJsonDto> Scopes);

    sealed record ScopeJsonDto(string Name, IReadOnlyList<DapVariableTreeNode> Variables);

    sealed record VariableListJsonDto(IReadOnlyList<DapVariableTreeNode> Variables);

    /// <summary>Один уровень детей (MCP: <c>debug_variable_children</c>).</summary>
    public static string SerializeVariableListToJson(IReadOnlyList<DapVariableTreeNode> oneLevel, bool writeIndented)
    {
        var options = writeIndented ? JsonDumpOptions : JsonDumpCompactOptions;
        return JsonSerializer.Serialize(new VariableListJsonDto(oneLevel), options);
    }

    static async Task<List<DapVariableTreeNode>> BuildTreeLevelAsync(
        DapClient client,
        JsonElement variables,
        int depth,
        int maxDepth,
        int maxChildrenPerNode,
        CancellationToken cancellationToken)
    {
        var list = new List<DapVariableTreeNode>();
        var total = variables.GetArrayLength();
        var n = 0;
        foreach (var v in variables.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (n >= maxChildrenPerNode)
                break;
            n++;
            var d = DapVariableDescriptor.FromVariableJson(v);
            List<DapVariableTreeNode>? children = null;
            if (depth < maxDepth && d.VariablesReference != 0)
            {
                var childrenBody = await FetchChildrenAsync(client, v, d.VariablesReference, maxChildrenPerNode, cancellationToken)
                    .ConfigureAwait(false);
                if (childrenBody != null &&
                    childrenBody.Value.TryGetProperty("variables", out var ch) &&
                    ch.GetArrayLength() > 0)
                {
                    children = await BuildTreeLevelAsync(client, ch, depth + 1, maxDepth, maxChildrenPerNode, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            list.Add(
                new DapVariableTreeNode
                {
                    Name = d.Name,
                    Value = d.Value,
                    Type = d.Type,
                    VariablesReference = d.VariablesReference,
                    NamedVariables = d.NamedVariables,
                    IndexedVariables = d.IndexedVariables,
                    Children = children is { Count: > 0 } ? children : null
                });
        }

        return list;
    }

    /// <summary>Текст для MCP (<c>debug_variables</c>): дерево с отступами.</summary>
    public static async Task AppendExpandedVariablesAsync(
        DapClient client,
        StringBuilder sb,
        JsonElement variables,
        string indent,
        int depth,
        int maxDepth,
        int maxChildrenPerNode,
        CancellationToken cancellationToken)
    {
        var total = variables.GetArrayLength();
        var n = 0;
        foreach (var v in variables.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (n >= maxChildrenPerNode)
            {
                var more = total - maxChildrenPerNode;
                if (more > 0)
                    sb.AppendLine($"{indent}… ({more} more)");
                break;
            }

            n++;
            await AppendOneVariableAsync(client, sb, v, indent, depth, maxDepth, maxChildrenPerNode, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Плоский список для панели отладки (имя с отступом дерева, значение и тип отдельно).</summary>
    public static async Task CollectExpandedVariablesAsync(
        DapClient client,
        List<(string Name, string Value, string? Type)> list,
        JsonElement variables,
        string indent,
        int depth,
        int maxDepth,
        int maxChildrenPerNode,
        CancellationToken cancellationToken)
    {
        var total = variables.GetArrayLength();
        var n = 0;
        foreach (var v in variables.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (n >= maxChildrenPerNode)
            {
                var more = total - maxChildrenPerNode;
                if (more > 0)
                    list.Add(($"{indent}…", $"{more} more", null));
                break;
            }

            n++;
            await CollectOneVariableAsync(client, list, v, indent, depth, maxDepth, maxChildrenPerNode, cancellationToken).ConfigureAwait(false);
        }
    }

    static async Task AppendOneVariableAsync(
        DapClient client,
        StringBuilder sb,
        JsonElement v,
        string indent,
        int depth,
        int maxDepth,
        int maxChildrenPerNode,
        CancellationToken cancellationToken)
    {
        var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
        var value = v.TryGetProperty("value", out var val) ? val.GetString() : null;
        var type = v.TryGetProperty("type", out var t) ? t.GetString() : null;
        var valueStr = value ?? "?";
        sb.AppendLine($"{indent}{name} = {valueStr}" + (type != null ? $" ({type})" : ""));

        if (depth >= maxDepth)
            return;
        if (!v.TryGetProperty("variablesReference", out var vrEl) || !vrEl.TryGetInt32(out var vref) || vref == 0)
            return;

        var childrenBody = await FetchChildrenAsync(client, v, vref, maxChildrenPerNode, cancellationToken).ConfigureAwait(false);
        if (childrenBody == null || !childrenBody.Value.TryGetProperty("variables", out var children) || children.GetArrayLength() == 0)
            return;

        await AppendExpandedVariablesAsync(client, sb, children, indent + "  ", depth + 1, maxDepth, maxChildrenPerNode, cancellationToken).ConfigureAwait(false);
    }

    static async Task CollectOneVariableAsync(
        DapClient client,
        List<(string Name, string Value, string? Type)> list,
        JsonElement v,
        string indent,
        int depth,
        int maxDepth,
        int maxChildrenPerNode,
        CancellationToken cancellationToken)
    {
        var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
        var value = v.TryGetProperty("value", out var val) ? val.GetString() : null;
        var type = v.TryGetProperty("type", out var t) ? t.GetString() : null;
        var valueStr = value ?? "?";
        list.Add(($"{indent}{name}", valueStr, type));

        if (depth >= maxDepth)
            return;
        if (!v.TryGetProperty("variablesReference", out var vrEl) || !vrEl.TryGetInt32(out var vref) || vref == 0)
            return;

        var childrenBody = await FetchChildrenAsync(client, v, vref, maxChildrenPerNode, cancellationToken).ConfigureAwait(false);
        if (childrenBody == null || !childrenBody.Value.TryGetProperty("variables", out var children) || children.GetArrayLength() == 0)
            return;

        await CollectExpandedVariablesAsync(client, list, children, indent + "  ", depth + 1, maxDepth, maxChildrenPerNode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Сначала запрос без фильтра; если детей нет — пробуем <c>indexed</c> и <c>named</c> (paging в DAP).</summary>
    static async Task<JsonElement?> FetchChildrenAsync(
        DapClient client,
        JsonElement parentVar,
        int variablesReference,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var body = await DapShared.WithRetryAsync(() => client.VariablesAsync(variablesReference, cancellationToken)).ConfigureAwait(false);
        if (HasNonEmptyVariables(body))
            return body;

        if (parentVar.TryGetProperty("indexedVariables", out var iv) && iv.ValueKind == JsonValueKind.Number && iv.TryGetInt32(out var ic) && ic > 0)
        {
            var take = Math.Min(ic, maxCount);
            body = await DapShared.WithRetryAsync(() =>
                client.VariablesAsync(variablesReference, filter: "indexed", start: 0, count: take, cancellationToken)).ConfigureAwait(false);
            if (HasNonEmptyVariables(body))
                return body;
        }

        if (parentVar.TryGetProperty("namedVariables", out var nv) && nv.ValueKind == JsonValueKind.Number && nv.TryGetInt32(out var nc) && nc > 0)
        {
            var take = Math.Min(nc, maxCount);
            body = await DapShared.WithRetryAsync(() =>
                client.VariablesAsync(variablesReference, filter: "named", start: 0, count: take, cancellationToken)).ConfigureAwait(false);
            if (HasNonEmptyVariables(body))
                return body;
        }

        return body;
    }

    static bool HasNonEmptyVariables(JsonElement? body)
    {
        if (body == null || !body.Value.TryGetProperty("variables", out var vars))
            return false;
        return vars.GetArrayLength() > 0;
    }

    /// <summary>
    /// Дети по <paramref name="variablesReference"/>; если пусто — повторяет DAP-fallback
    /// <c>indexed</c> / <c>named</c> (по подсказкам родителя, как в <see cref="FetchChildrenAsync"/>).
    /// </summary>
    public static async Task<JsonElement?> FetchChildVariablesBodyAsync(
        DapClient client,
        int variablesReference,
        int? parentNamedVariables,
        int? parentIndexedVariables,
        int maxChildren,
        CancellationToken cancellationToken)
    {
        var body = await DapShared.WithRetryAsync(() => client.VariablesAsync(variablesReference, cancellationToken))
            .ConfigureAwait(false);
        if (HasNonEmptyVariables(body))
            return body;

        if (parentIndexedVariables is { } ic && ic > 0)
        {
            var take = Math.Min(ic, maxChildren);
            body = await DapShared.WithRetryAsync(() =>
                    client.VariablesAsync(variablesReference, filter: "indexed", start: 0, count: take, cancellationToken))
                .ConfigureAwait(false);
            if (HasNonEmptyVariables(body))
                return body;
        }

        if (parentNamedVariables is { } nc && nc > 0)
        {
            var take = Math.Min(nc, maxChildren);
            body = await DapShared.WithRetryAsync(() =>
                    client.VariablesAsync(variablesReference, filter: "named", start: 0, count: take, cancellationToken))
                .ConfigureAwait(false);
            if (HasNonEmptyVariables(body))
                return body;
        }

        return body;
    }
}
