using System.Text.Json;
using AgentNotes.Core;

public sealed class ToolHandlers
{
    private readonly NotesStorage _storage;

    public ToolHandlers(NotesStorage storage)
    {
        _storage = storage;
    }

    public bool IsWriteLikeTool(string name) =>
        name is
            "write_agent_notes" or
            "append_agent_notes" or
            "upsert_agent_notes_section" or
            "delete_agent_notes_section" or
            "rollback_agent_notes" or
            "compact_hot_context" or
            "normalize_sections" or
            "write_knowledge_file" or
            "append_knowledge_file" or
            "upsert_knowledge_section" or
            "delete_knowledge_section" or
            "delete_knowledge_file";

    public string Handle(string toolName, IReadOnlyDictionary<string, JsonElement> args) =>
        toolName switch
        {
            "memory_health" => MemoryHealth(args),
            "route_context" => RouteContext(args),
            "write_agent_notes" => Write(args),
            "append_agent_notes" => Append(args),
            "read_agent_notes" => Read(args),
            "read_hot_context" => ReadHotContext(args),
            "upsert_agent_notes_section" => UpsertSection(args),
            "delete_agent_notes_section" => DeleteSection(args),
            "list_agent_notes_revisions" => ListRevisions(args),
            "rollback_agent_notes" => Rollback(args),
            "search_agent_notes" => Search(args),
            "extract_from_archive" => ExtractFromArchive(args),
            "compact_hot_context" => CompactHotContext(args),
            "validate_sections" => ValidateSections(args),
            "normalize_sections" => NormalizeSections(args),
            "write_knowledge_file" => WriteKnowledgeFile(args),
            "append_knowledge_file" => AppendKnowledgeFile(args),
            "upsert_knowledge_section" => UpsertKnowledgeSection(args),
            "delete_knowledge_section" => DeleteKnowledgeSection(args),
            "delete_knowledge_file" => DeleteKnowledgeFile(args),
            "read_knowledge_file" => ReadKnowledgeFile(args),
            "list_knowledge_files" => ListKnowledgeFiles(args),
            "knowledge_tags" => KnowledgeTags(args),
            "get_definition" => GetDefinition(args),
            "list_pack" => ListPack(args),
            "get_process" => GetProcess(args),
            "get_procedure" => GetProcedure(args),
            "radius_gate_check" => RadiusGateCheck(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}.")
        };

    private string MemoryHealth(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var activeScope = ToolArgs.OptionalString(args, "active_scope");
        return _storage.MemoryHealth(workspacePath, activeScope);
    }

    private string RouteContext(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var query = ToolArgs.RequiredString(args, "query");
        var activeScope = ToolArgs.OptionalString(args, "active_scope");
        var maxSections = ToolArgs.GetIntOrDefault(args, "max_sections", 5, 1, 20);
        var maxChars = ToolArgs.GetIntOrDefault(args, "max_chars", 12000, 1000, 40000);
        return _storage.RouteContext(workspacePath, query, activeScope, maxSections, maxChars);
    }

    private string Write(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var content = ToolArgs.RequiredString(args, "content");
        return _storage.Write(workspacePath, content);
    }

    private string Append(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var content = ToolArgs.RequiredString(args, "content");
        return _storage.Append(workspacePath, content);
    }

    private string Read(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        return _storage.Read(workspacePath);
    }

    private string UpsertSection(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var sectionId = ToolArgs.RequiredString(args, "section_id");
        var content = ToolArgs.RequiredString(args, "content");

        if (!ToolArgs.IsValidSectionId(sectionId))
            throw new ArgumentException("section_id must match ^[A-Za-z0-9._-]+$.");

        return _storage.UpsertSection(workspacePath, sectionId, content);
    }

    private string DeleteSection(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var sectionId = ToolArgs.RequiredString(args, "section_id");

        if (!ToolArgs.IsValidSectionId(sectionId))
            throw new ArgumentException("section_id must match ^[A-Za-z0-9._-]+$.");

        return _storage.DeleteSection(workspacePath, sectionId);
    }

    private string ListRevisions(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var limit = ToolArgs.GetIntOrDefault(args, "limit", 20, 1, 200);
        return _storage.ListRevisions(workspacePath, limit);
    }

    private string Rollback(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var revisionFile = ToolArgs.OptionalString(args, "revision_file");
        return _storage.Rollback(workspacePath, revisionFile);
    }

    private string Search(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var query = ToolArgs.RequiredString(args, "query");
        var limit = ToolArgs.GetIntOrDefault(args, "head_limit", 20, 1, 200);
        return _storage.Search(workspacePath, query, limit);
    }

    private string ReadHotContext(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var activeScope = ToolArgs.OptionalString(args, "active_scope");
        return _storage.ReadHotContext(workspacePath, activeScope);
    }

    private string ExtractFromArchive(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var query = ToolArgs.RequiredString(args, "query");
        var revisionFile = ToolArgs.OptionalString(args, "revision_file");
        var limit = ToolArgs.GetIntOrDefault(args, "head_limit", 10, 1, 100);
        var contextLines = ToolArgs.GetIntOrDefault(args, "context_lines", 2, 0, 20);
        return _storage.ExtractFromArchive(workspacePath, query, revisionFile, limit, contextLines);
    }

    private string CompactHotContext(IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        var apply = ToolArgs.GetBoolOrDefault(args, "apply", false);
        return _storage.CompactHotContext(workspacePath, apply);
    }

    private string ValidateSections(IReadOnlyDictionary<string, JsonElement> args)
    {
        var filePath = ToolArgs.OptionalString(args, "file_path");
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var knowledgePath = ToolArgs.OptionalString(args, "knowledge_path");
            var knowledgeRootId = ToolArgs.OptionalString(args, "knowledge_root_id");
            return _storage.ValidateKnowledgeSections(knowledgePath, filePath, knowledgeRootId);
        }

        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        return _storage.ValidateSections(workspacePath);
    }

    private string NormalizeSections(IReadOnlyDictionary<string, JsonElement> args)
    {
        var apply = ToolArgs.GetBoolOrDefault(args, "apply", false);
        var filePath = ToolArgs.OptionalString(args, "file_path");
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var knowledgePath = ToolArgs.OptionalString(args, "knowledge_path");
            var knowledgeRootId = ToolArgs.OptionalString(args, "knowledge_root_id");
            var saveRevision = ToolArgs.GetBoolOrDefault(args, "save_revision", true);
            return _storage.NormalizeKnowledgeSections(knowledgePath, filePath, apply, saveRevision, knowledgeRootId);
        }

        var workspacePath = ToolArgs.RequiredString(args, "workspace_path");
        return _storage.NormalizeSections(workspacePath, apply);
    }

    private string WriteKnowledgeFile(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var filePath = ToolArgs.RequiredString(args, "file_path");
        var content = ToolArgs.RequiredString(args, "content");
        var saveRevision = ToolArgs.GetBoolOrDefault(args, "save_revision", true);
        var allowShrink = ToolArgs.GetBoolOrDefault(args, "allow_shrink", false);
        return _storage.WriteKnowledgeFile(knowledgePath, filePath, content, saveRevision, knowledgeRootId, allowShrink);
    }

    private string AppendKnowledgeFile(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var filePath = ToolArgs.RequiredString(args, "file_path");
        var content = ToolArgs.RequiredString(args, "content");
        var saveRevision = ToolArgs.GetBoolOrDefault(args, "save_revision", true);
        return _storage.AppendKnowledgeFile(knowledgePath, filePath, content, saveRevision, knowledgeRootId);
    }

    private string UpsertKnowledgeSection(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var filePath = ToolArgs.RequiredString(args, "file_path");
        var sectionId = ToolArgs.RequiredString(args, "section_id");
        var content = ToolArgs.RequiredString(args, "content");
        var saveRevision = ToolArgs.GetBoolOrDefault(args, "save_revision", true);
        if (!ToolArgs.IsValidSectionId(sectionId))
            throw new ArgumentException("section_id must match ^[A-Za-z0-9._-]+$.");
        return _storage.UpsertKnowledgeSection(knowledgePath, filePath, sectionId, content, saveRevision, knowledgeRootId);
    }

    private string DeleteKnowledgeSection(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var filePath = ToolArgs.RequiredString(args, "file_path");
        var sectionId = ToolArgs.RequiredString(args, "section_id");
        if (!ToolArgs.IsValidSectionId(sectionId))
            throw new ArgumentException("section_id must match ^[A-Za-z0-9._-]+$.");
        return _storage.DeleteKnowledgeSection(knowledgePath, filePath, sectionId, knowledgeRootId);
    }

    private string DeleteKnowledgeFile(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var filePath = ToolArgs.RequiredString(args, "file_path");
        return _storage.DeleteKnowledgeFile(knowledgePath, filePath, knowledgeRootId);
    }

    private string ReadKnowledgeFile(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var filePath = ToolArgs.RequiredString(args, "file_path");
        var mode = ToolArgs.OptionalString(args, "mode");
        if (string.Equals(mode, "outline", StringComparison.OrdinalIgnoreCase))
        {
            var previewLines = ToolArgs.OptionalClampedInt(args, "preview_lines", 1, 40) ?? 5;
            return _storage.OutlineKnowledgeFile(knowledgePath, filePath, previewLines, knowledgeRootId);
        }

        // offset: first line to return, 1-based (like an editor). limit: max lines; 0 = empty; absent = to EOF.
        var offsetLine = ToolArgs.OptionalClampedInt(args, "offset", 1, 10_000_000);
        var limitLines = ToolArgs.OptionalClampedInt(args, "limit", 0, 10_000_000);
        if (offsetLine is null && limitLines is null)
            return _storage.ReadKnowledgeFile(knowledgePath, filePath, knowledgeRootId: knowledgeRootId);
        var from = offsetLine ?? 1;
        int? take = limitLines;
        return _storage.ReadKnowledgeFile(knowledgePath, filePath, from, take, knowledgeRootId);
    }

    private string ListKnowledgeFiles(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var subdir = ToolArgs.OptionalString(args, "subdir");
        return _storage.ListKnowledgeFiles(knowledgePath, subdir, knowledgeRootId);
    }

    private string KnowledgeTags(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var subdir = ToolArgs.OptionalString(args, "subdir");
        var tag = ToolArgs.OptionalString(args, "tag");
        var query = ToolArgs.OptionalString(args, "query");
        var mode = ToolArgs.OptionalString(args, "mode");
        var limit = ToolArgs.GetIntOrDefault(args, "limit", 50, 1, 500);
        var ssotOnly = ToolArgs.GetBoolOrDefault(args, "ssot_only", false);
        var includeRelated = ToolArgs.GetBoolOrDefault(args, "include_related", true);
        var refresh = ToolArgs.GetBoolOrDefault(args, "refresh", false);
        return _storage.QueryKnowledgeTags(
            knowledgePath,
            tag,
            subdir,
            knowledgeRootId,
            limit,
            mode,
            query,
            ssotOnly,
            includeRelated,
            refresh);
    }

    private string GetDefinition(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var definitionId = ToolArgs.RequiredString(args, "definition_id");
        var packId = ToolArgs.OptionalString(args, "pack_id");
        var packPath = ToolArgs.OptionalString(args, "pack_path");
        var allowedRoots = ToolArgs.OptionalStringArray(args, "allowed_roots");
        return _storage.GetDefinition(
            knowledgePath, definitionId, packId, packPath, knowledgeRootId, allowedRoots);
    }

    private string ListPack(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var packId = ToolArgs.OptionalString(args, "pack_id");
        var packPath = ToolArgs.OptionalString(args, "pack_path");
        var allowedRoots = ToolArgs.OptionalStringArray(args, "allowed_roots");
        return _storage.ListPack(knowledgePath, packId, packPath, knowledgeRootId, allowedRoots);
    }

    private string GetProcess(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var processId = ToolArgs.OptionalString(args, "process_id");
        var packId = ToolArgs.OptionalString(args, "pack_id");
        var packPath = ToolArgs.OptionalString(args, "pack_path");
        var allowedRoots = ToolArgs.OptionalStringArray(args, "allowed_roots");
        return _storage.GetProcess(
            knowledgePath, processId, packId, packPath, knowledgeRootId, allowedRoots);
    }

    private string GetProcedure(IReadOnlyDictionary<string, JsonElement> args)
    {
        var knowledgePath = ToolArgs.OptionalKnowledgePath(args);
        var knowledgeRootId = ToolArgs.OptionalKnowledgeRootId(args);
        var procedureId = ToolArgs.OptionalString(args, "procedure_id");
        var packId = ToolArgs.OptionalString(args, "pack_id");
        var packPath = ToolArgs.OptionalString(args, "pack_path");
        var allowedRoots = ToolArgs.OptionalStringArray(args, "allowed_roots");
        return _storage.GetProcedure(
            knowledgePath, procedureId, packId, packPath, knowledgeRootId, allowedRoots);
    }

    private string RadiusGateCheck(IReadOnlyDictionary<string, JsonElement> args)
    {
        double? delta = null;
        if (args.TryGetValue("delta_radius", out var d) && d.ValueKind is JsonValueKind.Number)
            delta = d.GetDouble();
        int? openCount = null;
        if (args.TryGetValue("open_hypothesis_count", out var o) && o.ValueKind is JsonValueKind.Number
            && o.TryGetInt32(out var oi))
            openCount = oi;
        var claim = ToolArgs.OptionalString(args, "claim");
        return _storage.RadiusGateCheck(delta, openCount, claim);
    }
}
