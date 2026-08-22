namespace Cdp.Core;

/// <summary>
/// Well-known language ids used by affordance seeds and engines.
/// Not a closed set — hosts may register more via <see cref="LanguageRegistry"/> / TOML.
/// </summary>
public static class CdpLanguages
{
    public const string Any = "any";
    public const string Csharp = "csharp";
    public const string Typescript = "typescript";
    public const string Python = "python";
    public const string Delphi = "delphi";
    public const string PowerShell = "powershell";

    public static bool IsAny(string? language) =>
        string.IsNullOrWhiteSpace(language)
        || language.Equals(Any, StringComparison.OrdinalIgnoreCase)
        || language == "*";
}

/// <summary>One detect rule for <see cref="LanguageRegistry.Detect"/>.</summary>
public sealed record LanguageDetectRule(
    string LanguageId,
    string Kind,
    int Priority,
    string? Extension = null,
    string? FileName = null);

/// <summary>
/// Language axis from config (not a closed enum): known ids, aliases, open/detect markers.
/// </summary>
public sealed class LanguageRegistry
{
    private readonly Dictionary<string, string> _aliasToId;
    private readonly List<LanguageDetectRule> _detect;
    private readonly string[] _ids;

    public LanguageRegistry(
        IEnumerable<string> ids,
        IEnumerable<KeyValuePair<string, string>> aliases,
        IEnumerable<LanguageDetectRule> detectRules)
    {
        _ids = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        _aliasToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _ids)
            _aliasToId[id] = id;

        foreach (var (alias, id) in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(id))
                continue;
            _aliasToId[alias.Trim()] = id.Trim().ToLowerInvariant();
        }

        _aliasToId[CdpLanguages.Any] = CdpLanguages.Any;
        _aliasToId["*"] = CdpLanguages.Any;

        _detect = detectRules
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Kind, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<string> Ids => _ids;

    public IReadOnlyList<LanguageDetectRule> DetectRules => _detect;

    /// <summary>Built-in defaults matching historical detector behaviour.</summary>
    public static LanguageRegistry Default { get; } = CreateDefault();

    /// <summary>Built-in defaults matching historical enum + detector behaviour.</summary>
    public static LanguageRegistry CreateDefault() => new(
        ids: [CdpLanguages.Csharp, CdpLanguages.Typescript, CdpLanguages.Python, CdpLanguages.Delphi, CdpLanguages.PowerShell],
        aliases:
        [
            new("cs", CdpLanguages.Csharp),
            new("c#", CdpLanguages.Csharp),
            new("ts", CdpLanguages.Typescript),
            new("tsx", CdpLanguages.Typescript),
            new("py", CdpLanguages.Python),
            new("pas", CdpLanguages.Delphi),
            new("objectpascal", CdpLanguages.Delphi),
            new("ps1", CdpLanguages.PowerShell),
            new("pwsh", CdpLanguages.PowerShell),
            new("ps", CdpLanguages.PowerShell),
        ],
        detectRules:
        [
            new(CdpLanguages.Csharp, "sln", 10, Extension: ".sln"),
            new(CdpLanguages.Csharp, "sln", 11, Extension: ".slnx"),
            new(CdpLanguages.Csharp, "csproj", 20, Extension: ".csproj"),
            new(CdpLanguages.Typescript, "tsconfig", 30, FileName: "tsconfig.json"),
            new(CdpLanguages.Python, "pyproject", 40, FileName: "pyproject.toml"),
            new(CdpLanguages.PowerShell, "ps1", 50, Extension: ".ps1"),
            new(CdpLanguages.PowerShell, "psm1", 51, Extension: ".psm1"),
        ]);

    public bool TryNormalize(string? raw, out string languageId)
    {
        languageId = "";
        if (raw is null)
            return false;
        var n = raw.Trim();
        if (n.Length == 0)
            return false;
        if (_aliasToId.TryGetValue(n, out var id))
        {
            languageId = id;
            return true;
        }

        // Unknown but non-empty: accept as-is (open catalog) so config can add engines without rebuild.
        languageId = n.ToLowerInvariant();
        return true;
    }

    public string NormalizeOrAny(string? raw) =>
        TryNormalize(raw, out var id) ? id : CdpLanguages.Any;

    public ProjectOpenResult Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required.", nameof(path));

        var full = Path.GetFullPath(path.Trim());
        if (File.Exists(full))
            return DetectFile(full);
        if (Directory.Exists(full))
            return DetectDirectory(full);

        throw new DirectoryNotFoundException($"Path not found: {full}");
    }

    private ProjectOpenResult DetectFile(string file)
    {
        var name = Path.GetFileName(file);
        var ext = Path.GetExtension(file);

        foreach (var rule in _detect)
        {
            if (rule.FileName is { } fn
                && name.Equals(fn, StringComparison.OrdinalIgnoreCase))
            {
                return ResultFor(Path.GetDirectoryName(file)!, rule, file);
            }

            if (rule.Extension is { } ex
                && ext.Equals(ex, StringComparison.OrdinalIgnoreCase))
            {
                return ResultFor(Path.GetDirectoryName(file)!, rule, file);
            }
        }

        return DetectDirectory(Path.GetDirectoryName(file)!);
    }

    private ProjectOpenResult DetectDirectory(string dir)
    {
        for (var cur = dir; cur is not null; cur = Directory.GetParent(cur)?.FullName)
        {
            foreach (var rule in _detect)
            {
                if (rule.FileName is { } fn)
                {
                    var candidate = Path.Combine(cur, fn);
                    if (File.Exists(candidate))
                        return ResultFor(cur, rule, candidate);
                }

                if (rule.Extension is { } ex)
                {
                    var hit = Directory.EnumerateFiles(cur, "*" + ex)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (hit is not null)
                        return ResultFor(cur, rule, hit);
                }
            }
        }

        return new ProjectOpenResult(dir, "any", CdpLanguages.Any, []);
    }

    private static ProjectOpenResult ResultFor(string root, LanguageDetectRule rule, string anchor)
    {
        string? solution = null;
        string? tsconfig = null;
        if (rule.LanguageId.Equals(CdpLanguages.Csharp, StringComparison.OrdinalIgnoreCase))
            solution = anchor;
        if (rule.LanguageId.Equals(CdpLanguages.Typescript, StringComparison.OrdinalIgnoreCase)
            || (rule.FileName?.Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase) ?? false))
            tsconfig = anchor;
        if (rule.LanguageId.Equals(CdpLanguages.PowerShell, StringComparison.OrdinalIgnoreCase))
            solution = anchor;

        return new ProjectOpenResult(root, rule.Kind, rule.LanguageId, [anchor], solution, tsconfig);
    }
}
