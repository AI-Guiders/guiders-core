using System.Text.Json.Serialization;

namespace DotnetDebug.Core;

/// <summary>Узел дерева переменных DAP для JSON (MCP <c>debug_variables</c> / дети по ref).</summary>
public sealed class DapVariableTreeNode
{
    public string Name { get; init; } = "";

    public string Value { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }

    public int VariablesReference { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NamedVariables { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IndexedVariables { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DapVariableTreeNode>? Children { get; init; }
}
