using System.Text;

namespace AgentNotes.Okf;

/// <summary>Verified provenance entry (OKF trust family, KB ADR 026 §2).</summary>
public sealed record OkfVerified(string By, string At);

/// <summary>Speech situation profile field (KB ADR 026 §3; canon `**Speech situation …**` lines).</summary>
public sealed record OkfSpeechSituation
{
    /// <summary>Primary speech situation pairs (audience, goal, genre, channel, stakes, distance, …).</summary>
    public IReadOnlyDictionary<string, string>? Primary { get; init; }

    /// <summary>Situation for citing to a human in another channel.</summary>
    public IReadOnlyDictionary<string, string>? WhenCitingToHuman { get; init; }

    /// <summary>Forbidden moves (e.g. <c>define_by_negation</c>).</summary>
    public IReadOnlyList<string>? ForbiddenMoves { get; init; }
}

/// <summary>
/// OKF frontmatter fields for one canon concept (KB ADR 026 §2–§3, M1 pilot subset).
/// Scalar values are emitted as single-line scalars, quoted when they contain
/// <c>: </c> or start with a YAML-special character; <see cref="ToYaml"/> rejects line breaks.
/// <see cref="TryParseYaml"/> reads back exactly what <see cref="ToYaml"/> emits.
/// </summary>
public sealed record OkfFrontmatter
{
    /// <summary>OKF required field: concept type (e.g. <c>Status</c>, <c>Playbook</c>, <c>Transfer Matrix</c>).</summary>
    public required string Type { get; init; }

    /// <summary>OKF recommended: concept title.</summary>
    public string? Title { get; init; }

    /// <summary>OKF recommended: one-line description.</summary>
    public string? Description { get; init; }

    /// <summary>OKF recommended: tags (mapped from the canon <c>**Tags:**</c> line).</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Profile: language contour (<c>ru</c> / <c>en</c> / <c>zh</c>).</summary>
    public string? Language { get; init; }

    /// <summary>Profile: visibility contour (<c>u</c> / <c>g</c> / <c>o</c>).</summary>
    public string? Contour { get; init; }

    /// <summary>Profile: memory layer (<c>hot-stub</c> / <c>l1</c> / <c>on-demand</c>).</summary>
    public string? Layer { get; init; }

    /// <summary>Profile: scope id the concept belongs to.</summary>
    public string? Scope { get; init; }

    /// <summary>Profile: world — first segment of the canon world slug (KB ADR 026 §3).</summary>
    public string? World { get; init; }

    /// <summary>Profile: subworld — remainder of the canon world slug; dotted id is <c>world.subworld</c>.</summary>
    public string? Subworld { get; init; }

    /// <summary>Profile: declared cross-world affiliations (dotted ids); routing hint, transfer policy stays in matrices.</summary>
    public IReadOnlyList<string>? RelatedWorlds { get; init; }

    /// <summary>OKF lifecycle: draft | stable | deprecated (mapped from canon Статус/Status lines).</summary>
    public string? Status { get; init; }

    /// <summary>Profile: section id registry — body markers `<!-- section:ID -->` remain normative for boundaries.</summary>
    public IReadOnlyList<string>? Sections { get; init; }

    /// <summary>Profile: lifecycle — concepts this one supersedes (lifted from card bullet lines).</summary>
    public IReadOnlyList<string>? Supersedes { get; init; }

    /// <summary>Profile: lifecycle — concept that supersedes this one.</summary>
    public IReadOnlyList<string>? SupersededBy { get; init; }

    /// <summary>OKF trust family: <c>Проверено:</c> provenance.</summary>
    public OkfVerified? Verified { get; init; }

    /// <summary>Profile: speech situation (structured, from canon <c>**Speech situation**</c> lines).</summary>
    public OkfSpeechSituation? SpeechSituation { get; init; }

    /// <summary>Emits the frontmatter body (without the <c>---</c> fences), terminated by a newline.</summary>
    public string ToYaml()
    {
        var sb = new StringBuilder();
        AppendScalar(sb, "type", Type);
        if (Title is not null)
        {
            AppendScalar(sb, "title", Title);
        }

        if (Description is not null)
        {
            AppendScalar(sb, "description", Description);
        }

        if (Tags is { Count: > 0 })
        {
            foreach (var tag in Tags)
            {
                RejectLineBreaks("tag", tag);
            }

            sb.Append("tags: [").Append(string.Join(", ", Tags)).Append(']').AppendLine();
        }

        if (Language is not null)
        {
            AppendScalar(sb, "language", Language);
        }

        if (Contour is not null)
        {
            AppendScalar(sb, "contour", Contour);
        }

        if (Layer is not null)
        {
            AppendScalar(sb, "layer", Layer);
        }

        if (Scope is not null)
        {
            AppendScalar(sb, "scope", Scope);
        }

        if (Verified is not null)
        {
            RejectLineBreaks("verified.by", Verified.By);
            RejectLineBreaks("verified.at", Verified.At);
            sb.Append("verified:").AppendLine()
              .Append("  - by: ").Append(QuoteIfNeeded(Verified.By)).AppendLine()
              .Append("    at: ").Append(QuoteIfNeeded(Verified.At)).AppendLine();
        }

        if (World is not null)
        {
            AppendScalar(sb, "world", World);
        }

        if (Subworld is not null)
        {
            AppendScalar(sb, "subworld", Subworld);
        }

        if (RelatedWorlds is { Count: > 0 })
        {
            sb.Append("related_worlds: [").Append(string.Join(", ", RelatedWorlds)).Append(']').AppendLine();
        }

        if (Status is not null)
        {
            AppendScalar(sb, "status", Status);
        }

        if (Sections is { Count: > 0 })
        {
            sb.Append("sections: [").Append(string.Join(", ", Sections)).Append(']').AppendLine();
        }

        if (Supersedes is { Count: > 0 })
        {
            foreach (var s in Supersedes)
            {
                RejectLineBreaks("supersedes", s);
            }

            sb.Append("supersedes: [").Append(string.Join(", ", Supersedes.Select(QuoteIfNeeded))).Append(']').AppendLine();
        }

        if (SupersededBy is not null)
        {
            foreach (var s in SupersededBy)
            {
                RejectLineBreaks("superseded_by", s);
            }

            sb.Append("superseded_by: [").Append(string.Join(", ", SupersededBy.Select(QuoteIfNeeded))).Append(']').AppendLine();
        }

        if (SpeechSituation is not null)
        {
            sb.Append("speech_situation:").AppendLine();
            foreach (var pair in SpeechSituation.Primary ?? new Dictionary<string, string>())
            {
                sb.Append("  ").Append(pair.Key).Append(": ").Append(QuoteIfNeeded(pair.Value)).AppendLine();
            }

            if (SpeechSituation.ForbiddenMoves is { Count: > 0 })
            {
                sb.Append("  forbidden_moves: [").Append(string.Join(", ", SpeechSituation.ForbiddenMoves)).Append(']').AppendLine();
            }

            if (SpeechSituation.WhenCitingToHuman is { Count: > 0 })
            {
                sb.Append("  when_citing_to_human:").AppendLine();
                foreach (var pair in SpeechSituation.WhenCitingToHuman)
                {
                    sb.Append("    ").Append(pair.Key).Append(": ").Append(QuoteIfNeeded(pair.Value)).AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>Parses the subset emitted by <see cref="ToYaml"/>. Total: never throws; unknown lines are ignored.</summary>
    public static OkfFrontmatter? TryParseYaml(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        string? type = null;
        string? title = null;
        string? description = null;
        IReadOnlyList<string>? tags = null;
        string? language = null;
        string? contour = null;
        string? layer = null;
        string? scope = null;
        OkfVerified? verified = null;
        string? world = null;
        string? subworld = null;
        IReadOnlyList<string>? relatedWorlds = null;
        string? status = null;
        IReadOnlyList<string>? sections = null;
        IReadOnlyList<string>? supersedes = null;
        IReadOnlyList<string>? supersededBy = null;
        Dictionary<string, string>? ssPrimary = null;
        Dictionary<string, string>? ssCite = null;
        List<string>? ssForbidden = null;
        string? section = null;

        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            if (line == "speech_situation:")
            {
                section = "ss";
                continue;
            }

            if (section is not null && line.StartsWith(' '))
            {
                var t = line.Trim();
                if (section == "ss" && t == "when_citing_to_human:")
                {
                    section = "ss-cite";
                }
                else if (TryKeyValue(t, out var skv))
                {
                    if (section == "ss")
                    {
                        if (skv.Key == "forbidden_moves" && skv.Value.StartsWith('[') && skv.Value.EndsWith(']'))
                        {
                            ssForbidden = skv.Value[1..^1].Split(',')
                                .Select(s => Unquote(s.Trim())).Where(s => s.Length > 0).ToList();
                        }
                        else
                        {
                            ssPrimary ??= [];
                            ssPrimary[skv.Key] = Unquote(skv.Value);
                        }
                    }
                    else
                    {
                        ssCite ??= [];
                        ssCite[skv.Key] = Unquote(skv.Value);
                    }
                }

                continue;
            }

            section = null;

            if (line == "verified:")
            {
                if (i + 2 < lines.Length)
                {
                    var byLine = lines[i + 1].Trim();
                    var atLine = lines[i + 2].Trim();
                    if (byLine.StartsWith("- ")
                        && TryKeyValue(byLine[2..].Trim(), out var by)
                        && by.Key == "by"
                        && TryKeyValue(atLine, out var at)
                        && at.Key == "at")
                    {
                        verified = new OkfVerified(Unquote(by.Value), Unquote(at.Value));
                        i += 2;
                    }
                }

                continue;
            }

            if (!TryKeyValue(line, out var kv))
            {
                continue;
            }

            switch (kv.Key)
            {
                case "type": type = Unquote(kv.Value); break;
                case "title": title = Unquote(kv.Value); break;
                case "description": description = Unquote(kv.Value); break;
                case "language": language = Unquote(kv.Value); break;
                case "contour": contour = Unquote(kv.Value); break;
                case "layer": layer = Unquote(kv.Value); break;
                case "scope": scope = Unquote(kv.Value); break;
                case "world": world = Unquote(kv.Value); break;
                case "subworld": subworld = Unquote(kv.Value); break;
                case "related_worlds" when kv.Value.StartsWith('[') && kv.Value.EndsWith(']'):
                    relatedWorlds = ParseList(kv.Value); break;
                case "status": status = Unquote(kv.Value); break;
                case "sections" when kv.Value.StartsWith('[') && kv.Value.EndsWith(']'):
                    sections = ParseList(kv.Value); break;
                case "supersedes" when kv.Value.StartsWith('[') && kv.Value.EndsWith(']'):
                    supersedes = ParseList(kv.Value); break;
                case "superseded_by" when kv.Value.StartsWith('[') && kv.Value.EndsWith(']'):
                    supersededBy = ParseList(kv.Value); break;
                case "tags" when kv.Value.StartsWith('[') && kv.Value.EndsWith(']'):
                    var inner = kv.Value[1..^1].Trim();
                    tags = inner.Length == 0
                        ? []
                        : inner.Split(',').Select(t => Unquote(t.Trim())).Where(t => t.Length > 0).ToArray();
                    break;
            }
        }

        if (type is null)
        {
            return null;
        }

        OkfSpeechSituation? speech = null;
        if (ssPrimary is not null || ssCite is not null || ssForbidden is not null)
        {
            speech = new OkfSpeechSituation
            {
                Primary = ssPrimary,
                WhenCitingToHuman = ssCite,
                ForbiddenMoves = ssForbidden,
            };
        }

        return new OkfFrontmatter
        {
            Type = type,
            Title = title,
            Description = description,
            Tags = tags,
            Language = language,
            Contour = contour,
            Layer = layer,
            Scope = scope,
            World = world,
            Subworld = subworld,
            RelatedWorlds = relatedWorlds,
            Status = status,
            Sections = sections,
            Supersedes = supersedes,
            SupersededBy = supersededBy,
            Verified = verified,
            SpeechSituation = speech,
        };
    }

    private static IReadOnlyList<string> ParseList(string bracketed)
    {
        var inner = bracketed[1..^1].Trim();
        return inner.Length == 0
            ? []
            : inner.Split(',').Select(t => Unquote(t.Trim())).Where(t => t.Length > 0).ToArray();
    }

    private static bool TryKeyValue(string line, out (string Key, string Value) kv)
    {
        kv = default;
        if (line.StartsWith(' ') || line.StartsWith('-'))
        {
            return false;
        }

        var idx = line.IndexOf(':');
        if (idx <= 0 || idx == line.Length - 1)
        {
            return false;
        }

        kv = (line[..idx].Trim(), line[(idx + 1)..].Trim());
        return kv.Key.Length > 0 && kv.Value.Length > 0;
    }

    private static void AppendScalar(StringBuilder sb, string key, string value)
    {
        RejectLineBreaks(key, value);
        sb.Append(key).Append(": ").Append(QuoteIfNeeded(value)).AppendLine();
    }

    private static void RejectLineBreaks(string field, string value)
    {
        if (value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException($"Frontmatter field '{field}' must be a single-line scalar.", field);
        }
    }

    private static string QuoteIfNeeded(string value)
        => value.Contains(": ") || value.Contains(" #") || IsYamlSpecialStart(value)
            ? "'" + value.Replace("'", "''") + "'"
            : value;

    private static bool IsYamlSpecialStart(string value)
    {
        var c = value[0];
        return c is '[' or '{' or '#' or '&' or '*' or '!' or '%' or '@' or '`' or '"' or '|' or '>' or '\'';
    }

    private static string Unquote(string value)
        => value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\'')
            ? value[1..^1].Replace("''", "'")
            : value;
}
