using System.Text.Encodings.Web;
using System.Text.Json;

namespace AgentNotes.Core;

/// <summary>
/// SSOT for KB payload serialization. Tool payloads are read by LLMs, not embedded into HTML:
/// the conservative default encoder costs ×2–3 tokens on Cyrillic/CJK (every char becomes
/// \uXXXX). Relaxed escaping is the MEAI / ASP.NET Core default.
/// </summary>
public static class AigJson
{
    public static JsonSerializerOptions Indented { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonSerializerOptions IndentedSnakeCase { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
