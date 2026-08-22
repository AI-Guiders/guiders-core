namespace AgentNotes.Core.Packs;

/// <summary>TOML meta for <c>pack/pack.toml</c>.</summary>
public sealed class PackTomlDocument
{
    public string? Id { get; set; }
    public string? Version { get; set; }
    public string? Title { get; set; }
    public string? Onboarding { get; set; }
    public string? Content { get; set; }
    public string? Route { get; set; }
    public List<string> Sources { get; set; } = [];
}

/// <summary>TOML route for <c>pack/processes.toml</c>.</summary>
public sealed class ProcessesTomlDocument
{
    public List<ProcessTomlEntry> Process { get; set; } = [];
}

public sealed class ProcessTomlEntry
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ApplyWhen { get; set; }
    public List<string> Signals { get; set; } = [];
    public List<string> Steps { get; set; } = [];
    public List<string> Gate { get; set; } = [];
    public List<string> DefinitionAnchors { get; set; } = [];
}

/// <summary>TOML when-cards for <c>pack/procedures.toml</c> (host-rule analogue).</summary>
public sealed class ProceduresTomlDocument
{
    public List<ProcedureTomlEntry> Procedure { get; set; } = [];
}

public sealed class ProcedureTomlEntry
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ApplyWhen { get; set; }
    public List<string> Signals { get; set; } = [];
    public List<string> Phases { get; set; } = [];
    public List<string> Steps { get; set; } = [];
    public List<string> Gate { get; set; } = [];
    public List<string> DefinitionAnchors { get; set; } = [];
    public string? RelatedProcess { get; set; }
    public List<string> ToolAnchors { get; set; } = [];
    public string? LlmCue { get; set; }
    public List<string> HostProjectors { get; set; } = [];
}

/// <summary>Parsed markdown card (<c>- key: value</c> lines).</summary>
public sealed class PackCard
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string RelativePath { get; init; }
    public required string Markdown { get; init; }
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
}
