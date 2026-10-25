using AgentNotes.Okf;

namespace AgentNotes.Okf.Cli;

/// <summary>
/// Usage: AgentNotes.Okf.Cli &lt;root-dir&gt; [--write]
/// Dry-run by default; --write applies in place and verifies idempotence with a second pass.
/// Skips .revisions/ and adr/. Files with a leading «НЕ ПУБЛИКОВАТЬ» line migrate to contour:u (KB ADR 026 §8).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: AgentNotes.Okf.Cli <root-dir> [--write]");
            return 2;
        }

        var write = args.Contains("--write", StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(args[0]);
        var files = Directory
            .EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(p => !p.Replace('/', '\\').Contains("\\.revisions\\", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Replace('/', '\\').Contains("\\adr\\", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var changed = 0;
        var unchanged = 0;
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var rel = Path.GetRelativePath(root, file);
            var (world, subworld) = WorldFor(file);
            string? contour = null;

            var body = text.StartsWith('\uFEFF') ? text[1..] : text;
            var firstLineEnd = body.IndexOf('\n');
            var firstLine = (firstLineEnd < 0 ? body : body[..firstLineEnd]).TrimEnd('\r');
            if (firstLine.Contains("НЕ ПУБЛИКОВАТЬ", StringComparison.Ordinal))
            {
                // Private-by-marker file (KB ADR 026 §8): marker line is replaced by contour:u.
                contour = "u";
                var afterMarker = firstLineEnd < 0 ? string.Empty : body[(firstLineEnd + 1)..];
                if (afterMarker.StartsWith("\r\n")) { afterMarker = afterMarker[2..]; }
                else if (afterMarker.StartsWith('\n')) { afterMarker = afterMarker[1..]; }
                text = (text.StartsWith('\uFEFF') ? "\uFEFF" : string.Empty) + afterMarker;
            }

            var migrated = CanonDocMigrator.Migrate(
                text,
                Path.GetFileName(file),
                CanonDocMigrator.TypeOverrideFor(Path.GetFullPath(file).Replace('\\', '/')),
                world,
                subworld,
                contour);
            if (migrated == text)
            {
                unchanged++;
                Console.WriteLine($"  = {rel}");
                continue;
            }

            changed++;
            Console.WriteLine($"{(write ? "M" : "~")} {rel}");
            if (write)
            {
                File.WriteAllText(file, migrated, new System.Text.UTF8Encoding(false));
            }
        }

        Console.WriteLine($"{files.Count} files, {changed} changed, {unchanged} already migrated.");

        if (write && changed > 0)
        {
            var drift = files.Count(file =>
            {
                var t = File.ReadAllText(file);
                return CanonDocMigrator.Migrate(t, Path.GetFileName(file)) != t;
            });
            Console.WriteLine(drift == 0 ? "Idempotence check: OK (second pass changes nothing)." : $"Idempotence check: FAILED ({drift} files still changing).");
            return drift == 0 ? 0 : 1;
        }

        return 0;
    }

    private static (string? World, string? Subworld) WorldFor(string fileFullPath)
    {
        var parts = fileFullPath.Replace('\\', '/').Split('/');
        var wi = Array.LastIndexOf(parts, "worlds");
        if (wi < 0 || wi + 1 >= parts.Length)
        {
            return (null, null);
        }

        var slug = parts[wi + 1];
        var dash = slug.IndexOf('-');
        return dash < 0
            ? (slug, null)
            : (slug[..dash], slug[(dash + 1)..]);
    }
}
