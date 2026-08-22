using System.Text.Json;

namespace DotnetDebug.Core;

/// <summary>Одна переменная из ответа DAP <c>variables</c> (для корня scope и детей при ленивом expand).</summary>
public readonly record struct DapVariableDescriptor(
    string Name,
    string Value,
    string? Type,
    int VariablesReference,
    int? NamedVariables,
    int? IndexedVariables)
{
    public static DapVariableDescriptor FromVariableJson(JsonElement v)
    {
        var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
        var value = v.TryGetProperty("value", out var val) ? val.GetString() : null;
        var type = v.TryGetProperty("type", out var t) ? t.GetString() : null;
        var vref = 0;
        if (v.TryGetProperty("variablesReference", out var vrEl) && vrEl.ValueKind == JsonValueKind.Number)
            vrEl.TryGetInt32(out vref);
        int? nv = null;
        if (v.TryGetProperty("namedVariables", out var nve) && nve.ValueKind == JsonValueKind.Number && nve.TryGetInt32(out var ni))
            nv = ni;
        int? iv = null;
        if (v.TryGetProperty("indexedVariables", out var ive) && ive.ValueKind == JsonValueKind.Number && ive.TryGetInt32(out var ii))
            iv = ii;
        return new DapVariableDescriptor(name, value ?? "?", type, vref, nv, iv);
    }
}
