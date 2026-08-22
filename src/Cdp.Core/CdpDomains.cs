namespace Cdp.Core;

/// <summary>Agent-facing layer (capabilities / TOML), not a CallTool prefix by itself.</summary>
public enum CdpLayer
{
    Memory,
    Dev,
    Ide
}

/// <summary>Wire domains and CallTool name splitting (longest-prefix).</summary>
public static class CdpDomains
{
    public const string MemoryWorld = "memory_world";
    public const string MemoryProject = "memory_project";
    public const string MemoryTask = "memory_task";
    public const string MemorySession = "memory_session";
    public const string MemorySkill = "memory_skill";
    public const string MemorySelfFinding = "memory_self_finding";
    public const string MemorySelfFailure = "memory_self_failure";
    public const string Debug = "debug";
    public const string Build = "build";
    public const string Roslyn = "roslyn";
    public const string Git = "git";
    /// <summary>Hybrid Codebase Index (HCI) — not human–computer interface.</summary>
    public const string CodebaseIndex = "codebase_index";
    public const string Anui = "anui";
    public const string Ide = "ide";

    /// <summary>Longest first — required for memory_self_finding vs memory_self / codebase_index.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        MemorySelfFinding,
        MemorySelfFailure,
        CodebaseIndex,
        MemoryWorld,
        MemoryProject,
        MemoryTask,
        MemorySession,
        MemorySkill,
        Debug,
        Build,
        Roslyn,
        Git,
        Anui,
        Ide
    ];

    public static CdpLayer LayerOf(string domain) => domain switch
    {
        MemoryWorld or MemoryProject or MemoryTask or MemorySession or MemorySkill
            or MemorySelfFinding or MemorySelfFailure => CdpLayer.Memory,
        Debug or Build or Roslyn or Git or CodebaseIndex or Anui => CdpLayer.Dev,
        Ide => CdpLayer.Ide,
        _ => CdpLayer.Memory
    };

    public static bool TrySplit(string toolName, out string domain, out string underlying)
    {
        domain = "";
        underlying = "";
        if (string.IsNullOrEmpty(toolName))
            return false;

        foreach (var d in All.OrderByDescending(x => x.Length))
        {
            if (toolName.Length > d.Length + 1
                && toolName.StartsWith(d, StringComparison.Ordinal)
                && toolName[d.Length] == '_')
            {
                domain = d;
                underlying = toolName[(d.Length + 1)..];
                return underlying.Length > 0;
            }
        }

        return false;
    }

    public static string Prefixed(string domain, string underlying)
    {
        // Catalog tools already named `{domain}_*` (debug_launch, git_scene, codebase_index_search)
        // must not become domain_domain_* on the wire.
        if (underlying.StartsWith(domain + "_", StringComparison.Ordinal))
            return underlying;
        return domain + "_" + underlying;
    }

    /// <summary>
    /// After <see cref="TrySplit"/>, restore catalog tool id when <see cref="Prefixed"/> collapsed
    /// a domain-prefixed underlying (legacy double-prefix CallTool names still work).
    /// </summary>
    public static string ExpandUnderlying(string domain, string underlying)
    {
        if (string.IsNullOrEmpty(underlying))
            return underlying;

        // Product catalogs that bake the domain into tool names.
        if (domain is not (Debug or Roslyn or Git or CodebaseIndex or Anui))
            return underlying;

        var prefix = domain + "_";
        return underlying.StartsWith(prefix, StringComparison.Ordinal)
            ? underlying
            : prefix + underlying;
    }
}
