using AgentNotes.Core;
using AgentNotes.Okf;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace AgentNotes.Okf.Tests;

/// <summary>
/// Legacy removal (KB ADR 026, M3 п.1): KnowledgeTags reads tags from the OKF frontmatter only.
/// Properties: round-trip with the mapper emitter, block-list tolerance, totality, role detection.
/// </summary>
public static class KnowledgeTagsFrontmatterProperties
{
    [Property(MaxTest = 200)]
    public static Property FrontmatterTagsRoundTrip(int[] seeds)
    {
        var tags = TagSeeds(seeds);
        var fm = new OkfFrontmatter { Type = "KB", Tags = tags.Length == 0 ? null : tags };
        var parsed = KnowledgeTags.ParseTags("---\n" + fm.ToYaml() + "---\nbody");
        return parsed.SequenceEqual(KnowledgeTags.NormalizeTags(tags)).ToProperty();
    }

    [Property]
    public static Property ParseIsTotalAndEmptyWithoutFrontmatter(string text)
    {
        var t = text ?? string.Empty;
        var firstLine = t.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n')[0].TrimEnd('\r');
        var hasNoFrontmatterFence = firstLine != "---";
        return (!hasNoFrontmatterFence || KnowledgeTags.ParseTags(t).Count == 0).ToProperty();
    }

    [Fact]
    public static void BlockListAndQuotedFormsParse()
    {
        var block = "---\ntags:\n  - ssot\n  - 'knowledge-engineering'\n  - \"ADR\"\n---\n";
        Assert.Equal(new[] { "adr", "knowledge-engineering", "ssot" }, KnowledgeTags.ParseTags(block));
    }

    [Fact]
    public static void MigratedCanonSampleResolvesSsotRole()
    {
        const string doc = "---\ntype: KB\ntitle: Sample\ndescription: x\ntags: [kb, ssot, philosophy-dao-chan, dao, l0]\n---\n";
        var tags = KnowledgeTags.ParseTags(doc);
        Assert.Equal(new[] { "kb", "ssot" }, KnowledgeTags.RoleTagsOf(tags));
        Assert.Equal(new[] { "dao", "l0", "philosophy-dao-chan" }, KnowledgeTags.TopicTags(tags));
    }

    [Fact]
    public static void SectionRegistryMirrorsBodyMarkers()
    {
        var doc = "# Map v1\n\n<!-- section:world-biology-v1 -->\n## Playbook: biology\nbody\n<!-- /section:world-biology-v1 -->\n\n<!-- section:world-biology-playbook-v1 -->\nmore\n<!-- /section:world-biology-playbook-v1 -->\n<!-- section:world-biology-v1 -->\ndup marker ignored\n<!-- /section:world-biology-v1 -->\n";
        var migrated = CanonDocMigrator.Migrate(doc, "map-world-life-subworlds.md");
        var yamlEnd = migrated.IndexOf("\n---\n", StringComparison.Ordinal);
        var fm = OkfFrontmatter.TryParseYaml(migrated[3..yamlEnd]);

        Assert.NotNull(fm);
        Assert.Equal(new[] { "world-biology-v1", "world-biology-playbook-v1" }, fm.Sections);
        Assert.Equal(3, SectionMarkerCount(migrated));
        Assert.Equal(migrated, CanonDocMigrator.Migrate(migrated, "map-world-life-subworlds.md"));
    }

    private static int SectionMarkerCount(string text)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf("<!-- section:", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += 13;
        }

        return count;
    }

    [Fact]
    public static void LegacyTagsLineIsIgnored()
    {
        const string legacy = "# Title\n\n**Tags:** #adcm #ssot #playbook\n\nbody";
        Assert.Empty(KnowledgeTags.ParseTags(legacy));
    }

    private static string[] TagSeeds(int[] seeds)
    {
        var result = new List<string>();
        foreach (var seed in seeds.Take(6))
        {
            var v = (uint)Math.Abs(seed == int.MinValue ? 17 : seed);
            var length = 1 + (int)(v % 10);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                v = v * 2654435761u + 0x9E3779B9u;
                chars[i] = "abcdefghijklmnopqrstuvwxyz0123456789-"[(int)(v % 37)];
            }

            result.Add(new string(chars));
        }

        return result.ToArray();
    }
}
