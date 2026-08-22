using System.Text.RegularExpressions;
using AgentNotes.Core.Configuration;

namespace AgentNotes.Core.Packs;

/// <summary>Discover and read LLM-native packs under <c>knowledge/**/pack/</c>.</summary>
public static class LlmNativePackReader
{
    private static readonly Regex CardFieldRegex = new(
        @"^\s*-\s+(?<key>[A-Za-z0-9_.-]+)\s*:\s*(?<value>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> DiscoverPackDirs(
        string knowledgeRepoRoot,
        IReadOnlyList<string>? allowedRoots = null)
    {
        var knowledgeDir = Path.Combine(knowledgeRepoRoot, "knowledge");
        if (!Directory.Exists(knowledgeDir))
            return [];

        var searchRoots = BuildSearchRoots(knowledgeDir, allowedRoots);
        var found = new List<string>();
        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var packToml in Directory.EnumerateFiles(root, "pack.toml", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(packToml);
                if (dir is null)
                    continue;
                var name = Path.GetFileName(dir);
                if (!string.Equals(name, "pack", StringComparison.OrdinalIgnoreCase))
                    continue;
                found.Add(dir);
            }
        }

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static PackTomlDocument? TryReadPackMeta(string packDir)
    {
        var path = Path.Combine(packDir, "pack.toml");
        if (!File.Exists(path))
            return null;
        return AgentNotesMcpToml.DeserializeFile<PackTomlDocument>(path);
    }

    public static ProcessesTomlDocument? TryReadProcesses(string packDir)
    {
        var path = Path.Combine(packDir, "processes.toml");
        if (!File.Exists(path))
            return null;
        return AgentNotesMcpToml.DeserializeFile<ProcessesTomlDocument>(path);
    }

    public static ProceduresTomlDocument? TryReadProcedures(string packDir)
    {
        var path = Path.Combine(packDir, "procedures.toml");
        if (!File.Exists(path))
            return null;
        return AgentNotesMcpToml.DeserializeFile<ProceduresTomlDocument>(path);
    }

    public static string? FindPackDir(
        string knowledgeRepoRoot,
        string? packId,
        string? packPath,
        IReadOnlyList<string>? allowedRoots = null)
    {
        if (!string.IsNullOrWhiteSpace(packPath))
        {
            var relative = NormalizeRelative(packPath);
            EnsureAllowed(relative, allowedRoots);
            var full = Path.Combine(knowledgeRepoRoot, "knowledge", relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "pack.toml")))
                return full;
            return null;
        }

        var dirs = DiscoverPackDirs(knowledgeRepoRoot, allowedRoots);
        if (string.IsNullOrWhiteSpace(packId))
            return dirs.Count == 1 ? dirs[0] : null;

        foreach (var dir in dirs)
        {
            var meta = TryReadPackMeta(dir);
            if (meta?.Id is { Length: > 0 } id
                && string.Equals(id, packId.Trim(), StringComparison.OrdinalIgnoreCase))
                return dir;
        }

        return null;
    }

    public static PackCard? TryReadCard(string packDir, string definitionId, string knowledgeRepoRoot)
    {
        var id = definitionId.Trim();
        if (id.Length == 0)
            return null;

        foreach (var folder in new[] { "definitions", "misconceptions" })
        {
            var relativeUnderPack = Path.Combine(folder, id + ".md");
            var full = Path.Combine(packDir, relativeUnderPack);
            if (!File.Exists(full))
                continue;

            var markdown = File.ReadAllText(full);
            var fields = ParseCardFields(markdown);
            var kind = fields.GetValueOrDefault("kind")
                ?? (folder == "misconceptions" ? "misconception" : "definition");
            var cardId = fields.GetValueOrDefault("id") ?? id;
            var relative = ToKnowledgeRelative(knowledgeRepoRoot, full);
            return new PackCard
            {
                Id = cardId,
                Kind = kind,
                RelativePath = relative,
                Markdown = markdown,
                Fields = fields
            };
        }

        return null;
    }

    public static PackCard? FindCardAcrossPacks(
        string knowledgeRepoRoot,
        string definitionId,
        string? packId,
        string? packPath,
        IReadOnlyList<string>? allowedRoots = null)
    {
        if (!string.IsNullOrWhiteSpace(packId) || !string.IsNullOrWhiteSpace(packPath))
        {
            var dir = FindPackDir(knowledgeRepoRoot, packId, packPath, allowedRoots);
            return dir is null ? null : TryReadCard(dir, definitionId, knowledgeRepoRoot);
        }

        foreach (var dir in DiscoverPackDirs(knowledgeRepoRoot, allowedRoots))
        {
            var card = TryReadCard(dir, definitionId, knowledgeRepoRoot);
            if (card is not null)
                return card;
        }

        return null;
    }

    public static IReadOnlyList<string> ListDefinitionIds(string packDir)
    {
        var defs = Path.Combine(packDir, "definitions");
        if (!Directory.Exists(defs))
            return [];
        return Directory.EnumerateFiles(defs, "*.md", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> ListMisconceptionIds(string packDir)
    {
        var defs = Path.Combine(packDir, "misconceptions");
        if (!Directory.Exists(defs))
            return [];
        return Directory.EnumerateFiles(defs, "*.md", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ToKnowledgeRelative(string knowledgeRepoRoot, string fullPath)
    {
        var knowledgeDir = Path.GetFullPath(Path.Combine(knowledgeRepoRoot, "knowledge"));
        var full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(knowledgeDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path outside knowledge/: {fullPath}");
        var rel = full[(knowledgeDir.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return rel.Replace('\\', '/');
    }

    public static Dictionary<string, string> ParseCardFields(string markdown)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            var m = CardFieldRegex.Match(line);
            if (!m.Success)
                continue;
            fields[m.Groups["key"].Value] = m.Groups["value"].Value;
        }

        return fields;
    }

    internal static IReadOnlyList<string> ParseAllowedRoots(IReadOnlyList<string>? roots)
    {
        if (roots is null || roots.Count == 0)
            return [];
        return roots
            .Select(NormalizeRelative)
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildSearchRoots(string knowledgeDir, IReadOnlyList<string>? allowedRoots)
    {
        var roots = ParseAllowedRoots(allowedRoots);
        if (roots.Count == 0)
            return [knowledgeDir];

        return roots
            .Select(r => Path.Combine(knowledgeDir, r.Replace('/', Path.DirectorySeparatorChar)))
            .ToArray();
    }

    private static void EnsureAllowed(string relative, IReadOnlyList<string>? allowedRoots)
    {
        var roots = ParseAllowedRoots(allowedRoots);
        if (roots.Count == 0)
            return;
        foreach (var root in roots)
        {
            if (relative.Equals(root, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return;
        }

        throw new ArgumentException(
            $"pack_path '{relative}' is outside allowed roots [{string.Join(", ", roots)}].");
    }

    private static string NormalizeRelative(string path)
    {
        var p = path.Replace('\\', '/').Trim().Trim('/');
        while (p.StartsWith("./", StringComparison.Ordinal))
            p = p[2..];
        if (p.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
            throw new ArgumentException($"Invalid relative path: {path}");
        return p;
    }
}
