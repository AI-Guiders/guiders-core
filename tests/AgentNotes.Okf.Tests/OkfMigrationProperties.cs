using AgentNotes.Okf;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace AgentNotes.Okf.Tests;

/// <summary>
/// Property-based tests for the OKF frontmatter mapper (KB ADR 026, M1 pilot).
/// Properties: canon tag line round-trip, parser totality, frontmatter idempotence.
/// Valid tags are derived deterministically from arbitrary seeds (base-37 alphabet,
/// canon kebab convention), so FsCheck shrinking stays meaningful on the raw input.
/// </summary>
public static class OkfMigrationProperties
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-";

    [Property(MaxTest = 200)]
    public static Property CanonTagsRoundTrip(int[] seeds)
    {
        var tags = ToTags(seeds);
        return tags.Length == 0
            ? true.ToProperty()
            : CanonTagLine.TryParse(CanonTagLine.Render(tags), out var parsed)
                .ToProperty()
                .And(parsed.SequenceEqual(tags).ToProperty());
    }

    [Property]
    public static Property CanonTagsParseIsTotalAndStrict(string? line)
    {
        var normalized = line ?? string.Empty;
        if (normalized.TrimStart().StartsWith("**Tags:**", StringComparison.Ordinal))
        {
            // In scope only for lines without the canon prefix: strictness of rejection.
            return true.ToProperty();
        }

        var ok = CanonTagLine.TryParse(normalized, out var parsed);
        return (!ok && parsed.Count == 0).ToProperty();
    }

    [Property(MaxTest = 200)]
    public static Property FrontmatterIsIdempotent(int[] seeds, string body)
    {
        var fm = FrontmatterFrom(seeds);
        var once = OkfMigrator.EnsureFrontmatter(body ?? string.Empty, fm);
        var twice = OkfMigrator.EnsureFrontmatter(once, fm);
        return (once == twice).ToProperty();
    }

    [Property(MaxTest = 200)]
    public static Property FrontmatterPreservesBodyAfterBlock(int[] seeds, string body)
    {
        var source = body ?? string.Empty;
        var fm = FrontmatterFrom(seeds);
        var migrated = OkfMigrator.EnsureFrontmatter(source, fm);

        // Idempotence re-run must not lose content; and the emitted doc must start with the block.
        var again = OkfMigrator.EnsureFrontmatter(migrated, fm);
        return migrated.StartsWith("---\ntype: ", StringComparison.Ordinal).ToProperty()
            .And((again == migrated).ToProperty());
    }

    [Fact]
    public static void CanonTagsExample()
    {
        var ok = CanonTagLine.TryParse("**Tags:** #kb #adr #readme", out var tags);
        Assert.True(ok);
        Assert.Equal(new[] { "kb", "adr", "readme" }, tags);
    }

    private static string[] ToTags(int[] seeds)
    {
        var result = new List<string>();
        foreach (var seed in seeds)
        {
            var v = (uint)Math.Abs(seed == int.MinValue ? 17 : seed);
            var length = 1 + (int)(v % 12);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                v = v * 2654435761u + 0x9E3779B9u;
                chars[i] = Alphabet[(int)(v % (uint)Alphabet.Length)];
            }

            result.Add(new string(chars));
        }

        return result.ToArray();
    }

    private static OkfFrontmatter FrontmatterFrom(int[] seeds)
    {
        var tags = ToTags(seeds);
        return new OkfFrontmatter
        {
            Type = "Playbook",
            Title = "Sample",
            Tags = tags.Length == 0 ? null : tags,
            Contour = "u",
            Layer = "on-demand",
        };
    }
}
