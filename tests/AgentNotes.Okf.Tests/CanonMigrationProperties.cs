using AgentNotes.Okf;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace AgentNotes.Okf.Tests;

/// <summary>
/// Properties for the full-document canon → OKF migration (KB ADR 026, M1 pilot):
/// YAML emit/parse round-trip, migration idempotence on pseudo-canon documents,
/// tags-line removal, title lifting, and the file-name → type registry.
/// </summary>
public static class CanonMigrationProperties
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -";

    [Property(MaxTest = 200)]
    public static Property FrontmatterYamlRoundTrip(int[] seeds)
    {
        var fm = FrontmatterFrom(seeds);
        var parsed = OkfFrontmatter.TryParseYaml(fm.ToYaml());
        var ok = parsed is not null
            && parsed.Type == fm.Type
            && parsed.Title == fm.Title
            && parsed.Description == fm.Description
            && (parsed.Tags ?? []).SequenceEqual(fm.Tags ?? [])
            && parsed.Language == fm.Language
            && parsed.World == fm.World
            && parsed.Subworld == fm.Subworld
            && (parsed.RelatedWorlds ?? []).SequenceEqual(fm.RelatedWorlds ?? [])
            && parsed.Status == fm.Status
            && (parsed.Sections ?? []).SequenceEqual(fm.Sections ?? [])
            && (parsed.Supersedes ?? []).SequenceEqual(fm.Supersedes ?? [])
            && parsed.Verified?.By == fm.Verified?.By
            && parsed.Verified?.At == fm.Verified?.At
            && DictsEqual(parsed.SpeechSituation?.Primary, fm.SpeechSituation?.Primary)
            && DictsEqual(parsed.SpeechSituation?.WhenCitingToHuman, fm.SpeechSituation?.WhenCitingToHuman)
            && (parsed.SpeechSituation?.ForbiddenMoves ?? []).SequenceEqual(fm.SpeechSituation?.ForbiddenMoves ?? []);
        return ok.ToProperty().Label($"yaml:\n{fm.ToYaml()}");
    }

    private static bool DictsEqual(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
        => (a ?? new Dictionary<string, string>()).OrderBy(kv => kv.Key)
            .SequenceEqual((b ?? new Dictionary<string, string>()).OrderBy(kv => kv.Key));

    [Property(MaxTest = 200)]
    public static Property FullMigrationIsIdempotent(int[] seeds, string body)
    {
        var doc = PseudoCanonDoc(seeds, body ?? string.Empty);
        var once = CanonDocMigrator.Migrate(doc, "playbook-sample.md");
        var twice = CanonDocMigrator.Migrate(once, "playbook-sample.md");
        return (once == twice).ToProperty()
            .Label($"first pass:\n{once}");
    }

    [Property(MaxTest = 200)]
    public static Property MigrationRemovesCanonTagsLineOnce(int[] seeds, string body)
    {
        var tags = ToTags(seeds);
        if (tags.Length == 0)
        {
            return true.ToProperty();
        }

        var doc = PseudoCanonDoc(seeds, body ?? string.Empty);
        var migrated = CanonDocMigrator.Migrate(doc, "note-sample.md");
        var tagsLineSurvived = migrated.Replace("\r\n", "\n").Split('\n')
            .Any(l => CanonTagLine.TryParse(l.Trim(), out _));
        return (!tagsLineSurvived).ToProperty()
            .Label($"canon tags line survived:\n{migrated[..Math.Min(400, migrated.Length)]}");
    }

    [Property(MaxTest = 200)]
    public static Property MigrationLiftsTitleAndKeepsBody(int[] seeds, string body)
    {
        var source = body ?? string.Empty;
        var doc = PseudoCanonDoc(seeds, source);
        var migrated = CanonDocMigrator.Migrate(doc, "kb-sample.md");
        var parsed = OkfFrontmatter.TryParseYaml(
            migrated[3..migrated.IndexOf("\n---\n", StringComparison.Ordinal)]);
        var bodySurvives = migrated.Replace("\r\n", "\n").Contains(source.Replace("\r\n", "\n").TrimEnd());
        return (parsed?.Title == "Sample title").ToProperty()
            .And(bodySurvives.ToProperty());
    }

    [Fact]
    public static void SpeechSituationAndInformalDescriptionFromCanon()
    {
        var doc = "**Tags:** #llm #definition\n"
            + "**Speech situation (primary):** `audience=agent_internal` · `goal=epistemic_gate` · `stakes=medium`\n"
            + "**Speech situation (when citing to human):** `audience=colleague` · `channel=cockpit_dialog`\n"
            + "**Speech situation (forbidden_moves):** `define_by_negation`, `faq_softening`\n"
            + "\n# Claim X\n\n- id: claim-x\n- informal: Разделять наблюдение, вывод и гипотезу.\n- kind: definition\n";
        var migrated = CanonDocMigrator.Migrate(doc, "claim-x.md");
        var yamlEnd = migrated.IndexOf("\n---\n", StringComparison.Ordinal);
        var fm = OkfFrontmatter.TryParseYaml(migrated[3..yamlEnd]);

        Assert.NotNull(fm);
        Assert.Equal("Claim X", fm.Title);
        Assert.Equal("Разделять наблюдение, вывод и гипотезу.", fm.Description);
        Assert.NotNull(fm.SpeechSituation);
        Assert.Equal("agent_internal", fm.SpeechSituation!.Primary?["audience"]);
        Assert.Equal("epistemic_gate", fm.SpeechSituation.Primary?["goal"]);
        Assert.Equal("medium", fm.SpeechSituation.Primary?["stakes"]);
        Assert.Equal("colleague", fm.SpeechSituation.WhenCitingToHuman?["audience"]);
        Assert.Equal(new[] { "define_by_negation", "faq_softening" }, fm.SpeechSituation.ForbiddenMoves);
        Assert.DoesNotContain("**Speech situation", migrated, StringComparison.Ordinal);
        Assert.DoesNotContain("**Tags:**", migrated, StringComparison.Ordinal);
    }

    [Fact]
    public static void StatusLinesMigrateWithPureRemoval()
    {
        var doc = "**Tags:** #x\nСтатус: active\n# Doc\n\n**Status:** Accepted · 2026-05-11\n\nBody text.\n";
        var migrated = CanonDocMigrator.Migrate(doc, "note-sample.md");
        var yamlEnd = migrated.IndexOf("\n---\n", StringComparison.Ordinal);
        var fm = OkfFrontmatter.TryParseYaml(migrated[3..yamlEnd]);

        Assert.NotNull(fm);
        Assert.Equal("stable", fm.Status);
        Assert.Equal("Doc", fm.Title);
        Assert.DoesNotContain("\nСтатус: active\n", migrated, StringComparison.Ordinal);
        Assert.Contains("**Status:** Accepted · 2026-05-11", migrated, StringComparison.Ordinal);
        var twice = CanonDocMigrator.Migrate(migrated, "note-sample.md");
        Assert.Equal(migrated, twice);
    }

    [Fact]
    public static void ContourOverrideEmitsAndRoundTrips()
    {
        var stripped = "# Diary\n\nPersonal body.\n";
        var migrated = CanonDocMigrator.Migrate(stripped, "note-diary.md", typeOverride: null, contourOverride: "u");
        var yamlEnd = migrated.IndexOf("\n---\n", StringComparison.Ordinal);
        var fm = OkfFrontmatter.TryParseYaml(migrated[3..yamlEnd]);

        Assert.NotNull(fm);
        Assert.Equal("u", fm!.Contour);
        Assert.Contains("contour: u", migrated, StringComparison.Ordinal);
        Assert.Equal(migrated, CanonDocMigrator.Migrate(migrated, "note-diary.md"));
    }

    [Fact]
    public static void ForumThreadTypeFromPath()
    {
        Assert.Equal("Thread", CanonDocMigrator.TypeOverrideFor("personal/LinesForum/topics/004-voice-letter-pleyada/00-thread.md"));
        Assert.Equal("Thread", CanonDocMigrator.TypeOverrideFor("topics/004-x/00-thread.md"));
        Assert.Null(CanonDocMigrator.TypeOverrideFor("personal/LinesForum/README.md"));
    }

    [Fact]
    public static void TableHeaderIsNotDescription()
    {
        var doc = "# Темы LinesForum\n\n| # | Тема | Статус |\n|---|---|---|\n| 1 | чартер | pinned |\n\nТекст после таблицы.\n";
        var migrated = CanonDocMigrator.Migrate(doc, "index.md");
        var yamlEnd = migrated.IndexOf("\n---\n", StringComparison.Ordinal);
        var fm = OkfFrontmatter.TryParseYaml(migrated[3..yamlEnd]);

        Assert.NotNull(fm);
        Assert.Equal("Index", fm!.Type);
        Assert.Equal("Текст после таблицы.", fm.Description);
    }

    [Fact]
    public static void TypeRegistryExamples()
    {
        Assert.Equal("Status", CanonDocMigrator.TypeFromFileName("status-knowledge-engineering.md"));
        Assert.Equal("Playbook", CanonDocMigrator.TypeFromFileName("playbook-php.md"));
        Assert.Equal("KB", CanonDocMigrator.TypeFromFileName("kb-psychology-fundamentals.md"));
        Assert.Equal("Do-Not-Transfer Matrix", CanonDocMigrator.TypeFromFileName("matrix-do-not-transfer.md"));
        Assert.Equal("Routing Matrix", CanonDocMigrator.TypeFromFileName("matrix-culture-routing.md"));
        Assert.Equal("Router", CanonDocMigrator.TypeFromFileName("index-knowledge-router.md"));
        Assert.Equal("Index", CanonDocMigrator.TypeFromFileName("README.md"));
        Assert.Equal("Note", CanonDocMigrator.TypeFromFileName("world-modeling-playbook.md"));
        Assert.Equal("Evidence Card", CanonDocMigrator.TypeFromFileName("kb-laozi-mawangdui-evidence.md"));
        Assert.Equal("ADR", CanonDocMigrator.TypeFromFileName("CDP-ADR-0206-habitat-integrity-subsystem.md"));
        Assert.Equal("ADR", CanonDocMigrator.TypeFromFileName("ANUI-ADR-0002-foreign-ingest-one-way.md"));
        Assert.Equal("Handoff", CanonDocMigrator.TypeFromFileName("handoff-line-dsh-2026-09-15.md"));
        Assert.Equal("Incarnation", CanonDocMigrator.TypeFromFileName("lyra-incarnation-2026-08-25-guiders-ui-adr.md"));
        Assert.Equal("Diary", CanonDocMigrator.TypeFromFileName("lyra-diary-2026-08-20-jupiter-blur-orbit.ru.md"));
        Assert.Equal("Project Card", CanonDocMigrator.TypeOverrideFor("work/projects/door-to-singularity/dash-spec/README.md"));
        Assert.Null(CanonDocMigrator.TypeOverrideFor("work/projects/README.md"));
        Assert.Equal("Scope Map", CanonDocMigrator.TypeOverrideFor("work/local/workspace-scope-map.md"));
        Assert.Equal("Agent Line", CanonDocMigrator.TypeOverrideFor("personal/assistantLines/tikhon/README.md"));
        Assert.Null(CanonDocMigrator.TypeOverrideFor("personal/assistantLines/README.md"));
    }

    private static string PseudoCanonDoc(int[] seeds, string body)
    {
        var tags = ToTags(seeds);
        var tagsLine = tags.Length == 0 ? string.Empty : CanonTagLine.Render(tags) + "\n";
        return tagsLine + "# Sample title\n" + body;
    }

    private static OkfFrontmatter FrontmatterFrom(int[] seeds)
    {
        var tags = ToTags(seeds);
        uint v = 2166136261u;
        foreach (var s in seeds)
        {
            v = (v ^ (uint)Math.Abs(s)) * 16777619u;
        }

        var hasVerified = seeds.Length > 0 && seeds[0] % 2 == 0;
        var hasSpeech = seeds.Length > 1;
        return new OkfFrontmatter
        {
            Type = "Playbook",
            Title = Text(seeds, 1 + (int)(v % 20)),
            Description = Text(seeds, 1 + (int)((v >> 5) % 30)),
            Tags = tags.Length == 0 ? null : tags,
            Language = "ru",
            World = "knowledge",
            Subworld = "engineering",
            RelatedWorlds = seeds.Length > 2 ? ["psychology.models", "systems.it"] : null,
            Status = seeds.Length > 3 ? "stable" : null,
            Sections = seeds.Length > 4 ? ["core-rule", "matrix", "red-flags"] : null,
            Supersedes = seeds.Length > 5 ? ["ADR-001", "note-old"] : null,
            Verified = hasVerified ? new OkfVerified("human:operator", DateFrom(seeds)) : null,
            SpeechSituation = hasSpeech
                ? new OkfSpeechSituation
                {
                    Primary = new Dictionary<string, string>
                    {
                        ["audience"] = "agent_internal",
                        ["goal"] = "epistemic_gate",
                        ["stakes"] = Text(seeds, 3),
                    },
                    WhenCitingToHuman = seeds.Length > 2
                        ? new Dictionary<string, string> { ["audience"] = "colleague" }
                        : null,
                    ForbiddenMoves = tags.Length == 0 ? null : tags.Take(2).ToList(),
                }
                : null,
        };
    }

    private static string Text(int[] seeds, int length)
    {
        var chars = new char[length];
        uint v = 2166136261u;
        for (var i = 0; i < chars.Length; i++)
        {
            v = v * 2654435761u + 0x9E3779B9u ^ (uint)(seeds.Length > 0 ? Math.Abs(seeds[i % seeds.Length]) : i);
            chars[i] = Alphabet[(int)(v % (uint)Alphabet.Length)];
        }

        var text = new string(chars).Trim();
        return text.Length == 0 ? "x" : text;
    }

    private static string DateFrom(int[] seeds)
    {
        uint v = (uint)Math.Abs(seeds[0]);
        var y = 2020 + (int)(v % 7);
        var m = 1 + (int)((v >> 3) % 12);
        var d = 1 + (int)((v >> 7) % 28);
        return $"{y:D4}-{m:D2}-{d:D2}";
    }

    private static string[] ToTags(int[] seeds)
    {
        var result = new List<string>();
        foreach (var seed in seeds.Take(6))
        {
            var v2 = (uint)Math.Abs(seed == int.MinValue ? 17 : seed);
            var length = 1 + (int)(v2 % 10);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                v2 = v2 * 2654435761u + 0x9E3779B9u;
                chars[i] = "abcdefghijklmnopqrstuvwxyz0123456789-"[(int)(v2 % 37)];
            }

            result.Add(new string(chars));
        }

        return result.ToArray();
    }
}
