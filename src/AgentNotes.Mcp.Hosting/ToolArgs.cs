using System.Text.Json;
using System.Text.RegularExpressions;

public static class ToolArgs
{
    internal static string RequiredString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var value))
            throw new ArgumentException($"{key} is required.");
        var str = value.GetString();
        if (string.IsNullOrWhiteSpace(str))
            throw new ArgumentException($"{key} is required.");
        return str.Trim();
    }

    internal static string? OptionalString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var value))
            return null;
        var str = value.GetString();
        return string.IsNullOrWhiteSpace(str) ? null : str.Trim();
    }

    /// <summary>Optional string array (JSON array of strings). Missing/empty → null.</summary>
    internal static IReadOnlyList<string>? OptionalStringArray(
        IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<string>();
        foreach (var el in value.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                continue;
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                list.Add(s.Trim());
        }

        return list.Count == 0 ? null : list;
    }

    internal static string? OptionalKnowledgePath(IReadOnlyDictionary<string, JsonElement> args) =>
        OptionalString(args, "knowledge_path");

    internal static string? OptionalKnowledgeRootId(IReadOnlyDictionary<string, JsonElement> args) =>
        OptionalString(args, "knowledge_root_id");

    internal static int GetIntOrDefault(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue, int min, int max)
    {
        if (!args.TryGetValue(key, out var raw))
            return defaultValue;

        int parsed;
        if (raw.ValueKind == JsonValueKind.Number)
        {
            parsed = raw.GetInt32();
        }
        else if (raw.ValueKind == JsonValueKind.String && int.TryParse(raw.GetString(), out var asInt))
        {
            parsed = asInt;
        }
        else
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, min, max);
    }

    /// <summary>Optional integer: key missing or null → null; otherwise parse number or string, then clamp to [min, max].</summary>
    internal static int? OptionalClampedInt(IReadOnlyDictionary<string, JsonElement> args, string key, int min, int max)
    {
        if (!args.TryGetValue(key, out var raw) || raw.ValueKind == JsonValueKind.Null)
            return null;
        int parsed;
        if (raw.ValueKind == JsonValueKind.Number)
            parsed = raw.GetInt32();
        else if (raw.ValueKind == JsonValueKind.String && int.TryParse(raw.GetString(), out var s))
            parsed = s;
        else
            return null;
        return Math.Clamp(parsed, min, max);
    }

    internal static bool IsValidSectionId(string sectionId) => Regex.IsMatch(sectionId, "^[A-Za-z0-9._-]+$");

    internal static bool GetBoolOrDefault(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var raw))
            return defaultValue;

        return raw.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(raw.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }
}
