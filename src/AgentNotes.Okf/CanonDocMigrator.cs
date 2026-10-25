using System.Text.RegularExpressions;

namespace AgentNotes.Okf;

/// <summary>
/// Full-document canon → OKF migration (KB ADR 026 §2, M1 pilot).
/// Derives frontmatter from canon conventions (file-name prefix → type, H1 → title,
/// first prose line → description, <c>**Tags:**</c> line → tags, <c>Проверено: date</c> → verified),
/// removes the canon tags line from the body, and inserts/updates the frontmatter block.
/// Idempotent: a second pass over an already migrated document changes nothing.
/// </summary>
public static partial class CanonDocMigrator
{
    [GeneratedRegex(@"(?:Проверено|Verified)\s*[）：:]?\s*(\d{4}-\d{2}-\d{2})")]
    private static partial Regex VerifiedRegex();

    [GeneratedRegex(@"^\*{0,2}(?:Проверено|Verified)\*{0,2}\s*[）：:]?\s*\d{4}-\d{2}-\d{2}\s*[.,;]?\s*$")]
    private static partial Regex VerifiedOnlyLineRegex();

    [GeneratedRegex(@"`(\w+)=([\w-]+)`")]
    private static partial Regex SpeechPairsRegex();

    [GeneratedRegex(@"`([\w-]+)`")]
    private static partial Regex SpeechTokensRegex();

    [GeneratedRegex(@"^\*{0,2}(?:Статус|Status)\*{0,2}\s*[：:]\s*([A-Za-z -]+?)(?=\s*[·,;]|$)")]
    private static partial Regex StatusWordRegex();

    [GeneratedRegex(@"^\*{0,2}(?:Статус|Status)\*{0,2}\s*[：:]\s*[A-Za-z -]+\s*[.,;]?\s*$")]
    private static partial Regex PureStatusLineRegex();

    [GeneratedRegex(@"<!-- section:([A-Za-z0-9._-]+) -->")]
    private static partial Regex SectionMarkerRegex();

    [GeneratedRegex(@"^-\s*supersedes:\s*(?<v>.+?)\s*$")]
    private static partial Regex SupersedesBulletRegex();

    [GeneratedRegex(@"^-\s*superseded_by:\s*(?<v>.+?)\s*$")]
    private static partial Regex SupersededByBulletRegex();

    [GeneratedRegex(@"^(WORK-)?(CDP|ANUI|CASA|GUIDERS)-ADR-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdrMirrorRegex();

    /// <summary>
    /// Old M2 defaults (Note/KB/Index) were produced by the weaker first-pass heuristic — the refined
    /// name heuristic may upgrade them. Authored/exotic types are preserved as-is.
    /// </summary>
    private static string? UpgradeHeuristicType(string? existingType, string fileName)
    {
        if (existingType is null)
        {
            return TypeFromFileName(fileName);
        }

        return existingType is "Note" or "KB" or "Index"
            ? TypeFromFileName(fileName)
            : existingType;
    }

    private static (IReadOnlyList<string> Supersedes, IReadOnlyList<string> SupersededBy) CollectLifecycle(string rest)
    {
        var sup = new List<string>();
        var by = new List<string>();
        foreach (var line in rest.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.Trim();
            var m = SupersedesBulletRegex().Match(t);
            if (m.Success)
            {
                var v = SanitizeScalar(m.Groups["v"].Value);
                if (v.Length > 0 && v != "—" && v != "-")
                {
                    sup.Add(v);
                }

                continue;
            }

            m = SupersededByBulletRegex().Match(t);
            if (m.Success)
            {
                var v = SanitizeScalar(m.Groups["v"].Value);
                if (v.Length > 0 && v != "—" && v != "-")
                {
                    by.Add(v);
                }
            }
        }

        return (sup, by);
    }

    private static IReadOnlyList<string> CollectSectionIds(string rest)
    {
        var ids = new List<string>();
        foreach (Match m in SectionMarkerRegex().Matches(rest))
        {
            if (!ids.Contains(m.Groups[1].Value))
            {
                ids.Add(m.Groups[1].Value);
            }
        }

        return ids;
    }

    private static string? NormalizeStatusWord(string word)
        => word.Trim().ToLowerInvariant() switch
        {
            "accepted" or "active" or "stable" or "актуален" => "stable",
            "proposed" or "draft" or "stub" or "living stub" => "draft",
            "deprecated" or "retired" or "архив" => "deprecated",
            _ => null,
        };

    private static string? FindStatus(string rest)
    {
        foreach (var line in rest.Replace("\r\n", "\n").Split('\n'))
        {
            var match = StatusWordRegex().Match(line.Trim());
            if (match.Success)
            {
                return NormalizeStatusWord(match.Groups[1].Value);
            }
        }

        return null;
    }

    private static bool IsStatusMetaLine(string trimmedLine)
        => StatusWordRegex().IsMatch(trimmedLine);

    /// <summary>Migrates one canon document. Total for any input; <paramref name="fileName"/> is used for the type heuristic.</summary>
    public static string Migrate(string markdown, string fileName)
        => Migrate(markdown, fileName, typeOverride: null, world: null, subworld: null);

    /// <summary>Migrates one canon document with optional type/world/subworld overrides (e.g. pack definitions → Definition Card; worlds/&lt;slug&gt;/ → world+subworld).</summary>
    public static string Migrate(string markdown, string fileName, string? typeOverride, string? world = null, string? subworld = null, string? contourOverride = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(fileName);

        var bom = markdown.StartsWith('\uFEFF') ? "\uFEFF" : string.Empty;
        var body = bom.Length == 0 ? markdown : markdown[1..];

        var existingEnd = FindFrontmatterEnd(body);
        var existingYaml = existingEnd == 0 ? null : ExtractYaml(body, existingEnd);
        var rest = body[existingEnd..];
        var existing = existingYaml is null ? null : OkfFrontmatter.TryParseYaml(existingYaml);

        var tags = CollectCanonTags(rest) ?? existing?.Tags;
        var title = FindTitle(rest) ?? existing?.Title;
        var description = FindDescription(rest, title) ?? existing?.Description;
        var verifiedAt = FindVerifiedDate(rest) ?? existing?.Verified?.At;

        var fm = new OkfFrontmatter
        {
            Type = typeOverride
                ?? UpgradeHeuristicType(existing?.Type, fileName),
            Title = title,
            Description = description,
            Tags = tags,
            Language = fileName.EndsWith(".ru.md", StringComparison.OrdinalIgnoreCase)
                ? existing?.Language ?? "ru"
                : existing?.Language,
            World = world ?? existing?.World,
            Subworld = subworld ?? existing?.Subworld,
            Contour = contourOverride ?? existing?.Contour,
            Verified = verifiedAt is null ? null : new OkfVerified("human:operator", verifiedAt),
            SpeechSituation = CollectSpeechSituation(rest) ?? existing?.SpeechSituation,
            Status = FindStatus(rest) ?? existing?.Status,
            Sections = CollectSectionIds(rest) is { Count: > 0 } ids ? ids : null,
            Supersedes = CollectLifecycle(rest).Supersedes is { Count: > 0 } sup ? sup : existing?.Supersedes,
            SupersededBy = CollectLifecycle(rest).SupersededBy is { Count: > 0 } by ? by : existing?.SupersededBy,
        };

        var cleaned = RemoveMigratedMetaLines(rest);
        return bom + "---\n" + fm.ToYaml() + "---\n" + cleaned;
    }

    /// <summary>Maps the canon file-name prefix to an OKF type (KB ADR 026 §9); unmatched names map to <c>Note</c>.</summary>
    public static string TypeFromFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase) || name.Equals("index.md", StringComparison.OrdinalIgnoreCase))
        {
            return "Index";
        }

        var first = name.Split('-')[0];
        return first.ToLowerInvariant() switch
        {
            "status" => "Status",
            "playbook" => "Playbook",
            "kb" => name.Contains("evidence", StringComparison.OrdinalIgnoreCase)
                ? "Evidence Card"
                : "KB",
            "matrix" => name.Contains("do-not-transfer", StringComparison.OrdinalIgnoreCase)
                ? "Do-Not-Transfer Matrix"
                : name.Contains("routing", StringComparison.OrdinalIgnoreCase) ? "Routing Matrix" : "Transfer Matrix",
            "map" => "Map",
            "runbook" => "Runbook",
            "note" => "Note",
            "index" => name.Contains("router", StringComparison.OrdinalIgnoreCase) ? "Router" : "Index",
            "checkpoint" => "Checkpoint",
            "kj" => "Journal",
            "incident" => "Incident",
            "article" => "Article",
            "book" => "Book",
            "digest" => "Digest",
            "template" => "Template",
            "handoff" => "Handoff",
            "glossary" => "Glossary",
            "maintenance" => "Maintenance Contract",
            _ => name.Contains("incarnation", StringComparison.OrdinalIgnoreCase)
                ? "Incarnation"
                : name.Contains("diary", StringComparison.OrdinalIgnoreCase) ? "Diary" : "Note",
        };
    }

    /// <summary>Path-based type overrides (KB ADR 026 §9/§10): pack cards, LinesForum, project cards, machine maps. Root-relative path with forward slashes.</summary>
    public static string? TypeOverrideFor(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');

        // Pack cards.
        if (normalized.Contains("/pack/definitions/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("definitions/", StringComparison.OrdinalIgnoreCase))
        {
            return "Definition Card";
        }

        if (normalized.Contains("/pack/misconceptions/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("misconceptions/", StringComparison.OrdinalIgnoreCase))
        {
            return "Misconception Card";
        }

        // LinesForum: topics/NNN-slug/00-thread.md → forum thread; topics/index.md stays the reserved listing.
        if ((normalized.Contains("/topics/", StringComparison.OrdinalIgnoreCase)
             || normalized.StartsWith("topics/", StringComparison.OrdinalIgnoreCase))
            && (normalized.EndsWith("/00-thread.md", StringComparison.OrdinalIgnoreCase)
                || normalized == "00-thread.md"))
        {
            return "Thread";
        }

        // Machine-local resolve maps (OQ-9): dictionary bodies stay untouched, only the type is declared.
        if (normalized.Contains("/work/local/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("work/local/", StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.EndsWith("/workspace-scope-map.md", StringComparison.OrdinalIgnoreCase)
                || normalized == "workspace-scope-map.md")
            {
                return "Scope Map";
            }

            if (normalized.EndsWith("/scope-alias-map.md", StringComparison.OrdinalIgnoreCase)
                || normalized == "scope-alias-map.md")
            {
                return "Scope Alias Map";
            }

            if (normalized.EndsWith("/active-scope.md", StringComparison.OrdinalIgnoreCase)
                || normalized == "active-scope.md")
            {
                return "State";
            }
        }

        // Project cards: work/projects/<scope>/<project-id>/README.md (hub README directly under projects/ stays Index).
        var wp = normalized.IndexOf("/work/projects/", StringComparison.OrdinalIgnoreCase);
        if (wp >= 0 || normalized.StartsWith("work/projects/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = normalized[(wp >= 0 ? wp + "/work/projects/".Length : "work/projects/".Length)..];
            if (rest.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase) && rest.Count(c => c == '/') == 2)
            {
                return "Project Card";
            }

            if (normalized.Contains("/subprojects/", StringComparison.OrdinalIgnoreCase))
            {
                return "Sub-Project Card";
            }
        }

        // Assistant lines: <line>/README.md is the line card (hub README under assistantLines/ root stays Index).
        var al = normalized.IndexOf("/assistantLines/", StringComparison.OrdinalIgnoreCase);
        if (al >= 0 || normalized.StartsWith("assistantLines/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = normalized[(al >= 0 ? al + "/assistantLines/".Length : "assistantLines/".Length)..];
            if (rest.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase) && rest.Count(c => c == '/') == 1)
            {
                return "Agent Line";
            }
        }

        if (normalized.Contains("/agent-harness-field-reports/", StringComparison.OrdinalIgnoreCase))
        {
            return "Field Report";
        }

        if (normalized.Contains("/recognition-history/", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase))
        {
            return "Recognition Map";
        }

        // Well-known META contracts (§9: Meta Contract).
        if (MetaContractNames.Contains(Path.GetFileName(normalized).ToLowerInvariant())
            && normalized.Contains("/META/", StringComparison.OrdinalIgnoreCase))
        {
            return "Meta Contract";
        }

        return null;
    }

    private static readonly HashSet<string> MetaContractNames = new(StringComparer.Ordinal)
    {
        "environment-axioms.md",
        "integrity-core.md",
        "integrity-post-spec.md",
        "awareness-as-countermeasure.md",
        "provenance-contract.md",
    };

    private static IReadOnlyList<string>? CollectCanonTags(string rest)
    {
        foreach (var line in rest.Replace("\r\n", "\n").Split('\n'))
        {
            if (CanonTagLine.TryParse(line, out var tags))
            {
                return tags;
            }
        }

        return null;
    }

    private static string? FindTitle(string rest)
    {
        foreach (var line in rest.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.Trim().TrimStart('\uFEFF').Trim();
            if (t.Length == 0 || CanonTagLine.TryParse(t, out _) || IsSpeechSituationLine(t) || IsVerifiedOnlyLine(t)
                || IsStatusMetaLine(t))
            {
                continue;
            }

            if (t.StartsWith("# "))
            {
                return SanitizeScalar(t[2..]);
            }

            // Any other heading or prose before an H1 — no H1 to lift.
            return null;
        }

        return null;
    }

    private static string? FindDescription(string rest, string? title)
    {
        foreach (var line in rest.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.Trim().TrimStart('\uFEFF').Trim();
            if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("<!--", StringComparison.Ordinal)
                || t.StartsWith('|') || t.StartsWith("---|", StringComparison.Ordinal)
                || CanonTagLine.TryParse(t, out _)
                || IsSpeechSituationLine(t) || IsVerifiedOnlyLine(t) || IsStatusMetaLine(t))
            {
                continue;
            }

            if (t.StartsWith("- informal:", StringComparison.Ordinal))
            {
                return SanitizeScalar(t["- informal:".Length..]);
            }

            if (t.StartsWith('-'))
            {
                continue;
            }

            var candidate = SanitizeScalar(t);
            if (title is not null && (candidate == title || candidate == "# " + title))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static bool IsSpeechSituationLine(string trimmedLine)
        => trimmedLine.StartsWith("**Speech situation", StringComparison.Ordinal);

    private static OkfSpeechSituation? CollectSpeechSituation(string rest)
    {
        Dictionary<string, string>? primary = null;
        Dictionary<string, string>? cite = null;
        List<string>? forbidden = null;
        foreach (var line in rest.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.Trim();
            if (!IsSpeechSituationLine(t))
            {
                continue;
            }

            if (t.StartsWith("**Speech situation (primary):**", StringComparison.Ordinal))
            {
                var matches = SpeechPairsRegex().Matches(t);
                if (matches.Count > 0)
                {
                    primary = matches
                        .GroupBy(m => m.Groups[1].Value)
                        .ToDictionary(g => g.Key, g => g.First().Groups[2].Value);
                }
            }
            else if (t.StartsWith("**Speech situation (when citing to human):**", StringComparison.Ordinal))
            {
                var matches = SpeechPairsRegex().Matches(t);
                if (matches.Count > 0)
                {
                    cite = matches
                        .GroupBy(m => m.Groups[1].Value)
                        .ToDictionary(g => g.Key, g => g.First().Groups[2].Value);
                }
            }
            else if (t.StartsWith("**Speech situation (forbidden_moves):**", StringComparison.Ordinal))
            {
                var tokens = SpeechTokensRegex().Matches(t)
                    .Where(m => !m.Groups[1].Value.Contains('='))
                    .Select(m => m.Groups[1].Value)
                    .ToList();
                if (tokens.Count > 0)
                {
                    forbidden = tokens;
                }
            }
        }

        return primary is null && cite is null && forbidden is null
            ? null
            : new OkfSpeechSituation { Primary = primary, WhenCitingToHuman = cite, ForbiddenMoves = forbidden };
    }

    private static string SanitizeScalar(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Replace("\uFEFF", string.Empty).Trim();

    private static string? FindVerifiedDate(string rest)
    {
        var match = VerifiedRegex().Match(rest);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string RemoveMigratedMetaLines(string rest)
    {
        var lines = rest.Replace("\r\n", "\n").Split('\n');
        var kept = lines.Where(l =>
        {
            var t = l.Trim();
            return !CanonTagLine.TryParse(t, out _)
                && !IsSpeechSituationLine(t)
                && !IsVerifiedOnlyLine(t)
                && !IsRemovableStatusLine(t)
                && !SupersedesBulletRegex().IsMatch(t)
                && !SupersededByBulletRegex().IsMatch(t);
        }).ToList();
        return string.Join('\n', kept);
    }

    private static bool IsRemovableStatusLine(string trimmedLine)
        => PureStatusLineRegex().IsMatch(trimmedLine)
            && StatusWordRegex().Match(trimmedLine) is { Success: true } m
            && NormalizeStatusWord(m.Groups[1].Value) is not null;

    private static bool IsVerifiedOnlyLine(string trimmedLine)
        => VerifiedOnlyLineRegex().IsMatch(trimmedLine);

    private static int FindFrontmatterEnd(string body)
    {
        var firstLineEnd = body.IndexOf('\n');
        var firstLine = (firstLineEnd < 0 ? body : body[..firstLineEnd]).TrimEnd('\r');
        if (firstLine != "---")
        {
            return 0;
        }

        var pos = firstLineEnd < 0 ? body.Length : firstLineEnd + 1;
        while (pos < body.Length)
        {
            var nl = body.IndexOf('\n', pos);
            var line = (nl < 0 ? body[pos..] : body[pos..nl]).TrimEnd('\r');
            if (line == "---")
            {
                return nl < 0 ? body.Length : nl + 1;
            }

            if (nl < 0)
            {
                return 0;
            }

            pos = nl + 1;
        }

        return 0;
    }

    private static string? ExtractYaml(string body, int blockEnd)
    {
        var nl = body.IndexOf('\n');
        var startYaml = nl < 0 ? body.Length : nl + 1;
        var end = blockEnd;
        while (end > startYaml && (body[end - 1] == '\n' || body[end - 1] == '\r'))
        {
            end--;
        }

        var lastFence = body.LastIndexOf("---\n", end, StringComparison.Ordinal);
        var yamlEnd = lastFence > startYaml ? lastFence : end;
        return yamlEnd <= startYaml ? null : body[startYaml..yamlEnd];
    }
}
