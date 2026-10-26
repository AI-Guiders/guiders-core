using System.Text.Json;

namespace AgentNotes.Core;

/// <summary>
/// Tag semantics for OKF concepts (KB ADR 026): tags live in the frontmatter
/// <c>tags:</c> field (flow list or block list). Role tags (<c>#ssot</c>, <c>#playbook</c>, …)
/// classify the concept; the rest are topic tags (playbook-kb-topic-hashtags-v1).
/// The legacy <c>**Tags:**</c> body line was removed by the OKF migration (M2) — no dual form.
/// </summary>
public static partial class KnowledgeTags
{
    public const string RoleSsot = "ssot";
    public const string RoleResearch = "research";
    public const string RoleLiving = "living";
    public const string RolePlaybook = "playbook";
    public const string RoleAdr = "adr";
    public const string RoleNote = "note";
    public const string RoleTemplate = "template";
    public const string RoleReadme = "readme";
    public const string RoleKb = "kb";
    public const string RoleMap = "map";
    public const string RoleStatus = "status";

    private static readonly HashSet<string> RoleTags = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleSsot, RoleResearch, RoleLiving, RolePlaybook, RoleAdr, RoleNote,
        RoleTemplate, RoleReadme, RoleKb, RoleMap, RoleStatus
    };

    /// <summary>
    /// Parses tags from the leading OKF frontmatter block. Total: never throws for any input;
    /// returns an empty list when there is no frontmatter or no <c>tags:</c> key.
    /// Supports the flow form (<c>tags: [a, b]</c>) and the block form (<c>tags:</c> followed by <c>- a</c> lines).
    /// </summary>
    public static IReadOnlyList<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var body = text.StartsWith('\uFEFF') ? text[1..] : text;
        var lines = body.Replace("\r\n", "\n").Split('\n');
        if ((lines.Length == 0 ? body : lines[0].TrimEnd('\r')) != "---")
        {
            return [];
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line == "---")
            {
                return [];
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("tags:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed[5..].Trim();
            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                return NormalizeTags(SplitFlowList(value[1..^1]));
            }

            if (value.Length > 0)
            {
                return NormalizeTags([value]);
            }

            var block = new List<string>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                var item = lines[j].TrimEnd('\r').Trim();
                if (item.StartsWith("- ", StringComparison.Ordinal))
                {
                    block.Add(item[2..].Trim());
                }
                else if (item.Length == 0)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }

            return NormalizeTags(block);
        }

        return [];
    }

    public static bool IsRoleTag(string tag)
    {
        var t = NormalizeOne(tag);
        return t is not null && RoleTags.Contains(t);
    }

    public static IReadOnlyList<string> TopicTags(IEnumerable<string> tags) =>
        tags.Select(NormalizeOne).Where(t => t is not null && !RoleTags.Contains(t!)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<string> RoleTagsOf(IEnumerable<string> tags) =>
        tags.Select(NormalizeOne).Where(t => t is not null && RoleTags.Contains(t!)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string> raw) =>
        raw.Select(NormalizeOne).Where(t => t is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string? NormalizeOne(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var t = raw.Trim().TrimStart('#').Trim('\'', '"');
        if (t.Length == 0 || !char.IsAsciiLetter(t[0]))
        {
            return null;
        }

        return t.ToLowerInvariant();
    }

    public static string ToJson(object payload) =>
        JsonSerializer.Serialize(payload, AigJson.Indented);

    private static IEnumerable<string> SplitFlowList(string inner)
        => inner.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0);
}
