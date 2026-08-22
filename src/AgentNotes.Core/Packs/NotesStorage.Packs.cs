using System.Text.Json;
using AgentNotes.Core.Packs;

namespace AgentNotes.Core;

public sealed partial class NotesStorage
{
    /// <summary>Read one LLM-native definition/misconception card from a pack.</summary>
    public string GetDefinition(
        string? knowledgePath,
        string definitionId,
        string? packId = null,
        string? packPath = null,
        string? knowledgeRootId = null,
        IReadOnlyList<string>? allowedRoots = null)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("definition_id is required.");

        var root = ResolveKnowledgeRoot(knowledgePath, knowledgeRootId);
        var card = LlmNativePackReader.FindCardAcrossPacks(
            root, definitionId.Trim(), packId, packPath, allowedRoots);

        if (card is null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "definition_not_found",
                definition_id = definitionId.Trim(),
                pack_id = packId,
                pack_path = packPath
            }, JsonOptions);
        }

        var packRel = ExtractPackRelative(card.RelativePath);
        var meta = packRel is null
            ? null
            : LlmNativePackReader.TryReadPackMeta(
                Path.Combine(root, "knowledge", packRel.Replace('/', Path.DirectorySeparatorChar)));

        return JsonSerializer.Serialize(new
        {
            ok = true,
            definition_id = card.Id,
            kind = card.Kind,
            pack_id = meta?.Id,
            pack_path = packRel,
            file_path = card.RelativePath,
            llm_cue = card.Fields.GetValueOrDefault("llm_cue"),
            informal = card.Fields.GetValueOrDefault("informal"),
            fields = card.Fields,
            markdown = card.Markdown
        }, JsonOptions);
    }

    private static string? ExtractPackRelative(string cardRelativePath)
    {
        var packSlash = cardRelativePath.LastIndexOf("/pack/", StringComparison.OrdinalIgnoreCase);
        return packSlash >= 0 ? cardRelativePath[..(packSlash + "/pack".Length)] : null;
    }

    /// <summary>List pack meta + definition/process/procedure ids.</summary>
    public string ListPack(
        string? knowledgePath,
        string? packId = null,
        string? packPath = null,
        string? knowledgeRootId = null,
        IReadOnlyList<string>? allowedRoots = null)
    {
        var root = ResolveKnowledgeRoot(knowledgePath, knowledgeRootId);

        if (!string.IsNullOrWhiteSpace(packId) || !string.IsNullOrWhiteSpace(packPath))
        {
            var dir = LlmNativePackReader.FindPackDir(root, packId, packPath, allowedRoots);
            if (dir is null)
            {
                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = "pack_not_found",
                    pack_id = packId,
                    pack_path = packPath
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(DescribePack(root, dir), JsonOptions);
        }

        var packs = LlmNativePackReader.DiscoverPackDirs(root, allowedRoots)
            .Select(d => DescribePack(root, d))
            .ToArray();
        return JsonSerializer.Serialize(new { ok = true, total = packs.Length, packs }, JsonOptions);
    }

    /// <summary>Read a guided process from <c>processes.toml</c>.</summary>
    public string GetProcess(
        string? knowledgePath,
        string? processId = null,
        string? packId = null,
        string? packPath = null,
        string? knowledgeRootId = null,
        IReadOnlyList<string>? allowedRoots = null)
    {
        var root = ResolveKnowledgeRoot(knowledgePath, knowledgeRootId);
        var effectivePackId = string.IsNullOrWhiteSpace(packId) && string.IsNullOrWhiteSpace(packPath)
            ? "epistemic-scene"
            : packId;
        var dir = LlmNativePackReader.FindPackDir(root, effectivePackId, packPath, allowedRoots);
        if (dir is null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "pack_not_found",
                pack_id = effectivePackId,
                pack_path = packPath
            }, JsonOptions);
        }

        var processes = LlmNativePackReader.TryReadProcesses(dir);
        var meta = LlmNativePackReader.TryReadPackMeta(dir);
        var packRel = LlmNativePackReader.ToKnowledgeRelative(root, dir);
        if (processes is null || processes.Process.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "processes_missing",
                pack_id = meta?.Id,
                pack_path = packRel
            }, JsonOptions);
        }

        var effectiveProcessId = string.IsNullOrWhiteSpace(processId)
            ? "bug-radius-shrink"
            : processId.Trim();
        var entry = processes.Process.FirstOrDefault(p =>
            string.Equals(p.Id, effectiveProcessId, StringComparison.OrdinalIgnoreCase));
        if (entry is null && processes.Process.Count == 1 && string.IsNullOrWhiteSpace(processId))
            entry = processes.Process[0];
        if (entry is null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "process_not_found",
                process_id = effectiveProcessId,
                pack_id = meta?.Id,
                available = processes.Process.Select(p => p.Id).ToArray()
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            pack_id = meta?.Id,
            pack_path = packRel,
            process = new
            {
                id = entry.Id,
                name = entry.Name,
                apply_when = entry.ApplyWhen,
                signals = entry.Signals,
                steps = entry.Steps,
                gate = entry.Gate,
                definition_anchors = entry.DefinitionAnchors
            },
            effectiveness = new
            {
                gate_step = "delta_radius < 0",
                gate_done = "radius == 0",
                llm_cue = "Does this conclusion shrink debug-radius? If not — do not promote."
            },
            suggested_next = new
            {
                policy = "ask",
                note = "No CIDE host enqueue yet — Agent Env asks before promote; auto later.",
                candidates = new object[]
                {
                    new { kind = "tool", name = "get_definition", definition_id = "debug-radius" },
                    new { kind = "tool", name = "get_definition", definition_id = "blast-radius" },
                    new { kind = "tool", name = "radius_gate_check", hint = "before promote claim" },
                    new { kind = "cue", text = "Δradius < 0? / blast known before write?" }
                }
            }
        }, JsonOptions);
    }

    /// <summary>Read a when-card procedure from <c>procedures.toml</c>.</summary>
    public string GetProcedure(
        string? knowledgePath,
        string? procedureId = null,
        string? packId = null,
        string? packPath = null,
        string? knowledgeRootId = null,
        IReadOnlyList<string>? allowedRoots = null)
    {
        var root = ResolveKnowledgeRoot(knowledgePath, knowledgeRootId);
        var effectivePackId = string.IsNullOrWhiteSpace(packId) && string.IsNullOrWhiteSpace(packPath)
            ? "epistemic-scene"
            : packId;
        var dir = LlmNativePackReader.FindPackDir(root, effectivePackId, packPath, allowedRoots);
        if (dir is null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "pack_not_found",
                pack_id = effectivePackId,
                pack_path = packPath
            }, JsonOptions);
        }

        var procedures = LlmNativePackReader.TryReadProcedures(dir);
        var meta = LlmNativePackReader.TryReadPackMeta(dir);
        var packRel = LlmNativePackReader.ToKnowledgeRelative(root, dir);
        if (procedures is null || procedures.Procedure.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "procedures_missing",
                pack_id = meta?.Id,
                pack_path = packRel
            }, JsonOptions);
        }

        var effectiveProcedureId = string.IsNullOrWhiteSpace(procedureId)
            ? "kolb-journal-park"
            : procedureId.Trim();
        var entry = procedures.Procedure.FirstOrDefault(p =>
            string.Equals(p.Id, effectiveProcedureId, StringComparison.OrdinalIgnoreCase));
        if (entry is null && procedures.Procedure.Count == 1 && string.IsNullOrWhiteSpace(procedureId))
            entry = procedures.Procedure[0];
        if (entry is null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "procedure_not_found",
                procedure_id = effectiveProcedureId,
                pack_id = meta?.Id,
                available = procedures.Procedure.Select(p => p.Id).ToArray()
            }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            ok = true,
            pack_id = meta?.Id,
            pack_path = packRel,
            procedure = new
            {
                id = entry.Id,
                name = entry.Name,
                apply_when = entry.ApplyWhen,
                signals = entry.Signals,
                phases = entry.Phases,
                steps = entry.Steps,
                gate = entry.Gate,
                definition_anchors = entry.DefinitionAnchors,
                related_process = entry.RelatedProcess,
                tool_anchors = entry.ToolAnchors,
                llm_cue = entry.LlmCue,
                host_projectors = entry.HostProjectors
            },
            suggested_next = new
            {
                policy = "ask",
                note = "Procedure is a when-card (ADR-0003); host .mdc is projector only.",
                candidates = new object[]
                {
                    new { kind = "tool", name = "get_definition", definition_id = entry.DefinitionAnchors.FirstOrDefault() ?? "kolb-journal" },
                    new { kind = "tool", name = "get_process", process_id = entry.RelatedProcess ?? "curiosity-kolb-loop" },
                    new { kind = "cue", text = entry.LlmCue ?? "Follow procedure steps; do not chat-only." }
                }
            }
        }, JsonOptions);
    }

    /// <summary>Agent-side Δradius gate (no CIDE UI).</summary>
    public string RadiusGateCheck(
        double? deltaRadius,
        int? openHypothesisCount = null,
        string? claim = null)
    {
        if (deltaRadius is null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "delta_radius_required",
                policy = "ask",
                llm_cue = "Before promote: Δradius < 0?"
            }, JsonOptions);
        }

        var shrinks = deltaRadius.Value < 0;
        var done = openHypothesisCount is 0;
        string verdict;
        string policy;
        if (done && shrinks)
        {
            verdict = "done_bug";
            policy = "ask";
        }
        else if (shrinks)
        {
            verdict = "step_ok";
            policy = "continue";
        }
        else
        {
            verdict = "step_wasted";
            policy = "ask";
        }

        return JsonSerializer.Serialize(new
        {
            ok = shrinks,
            verdict,
            policy,
            delta_radius = deltaRadius.Value,
            open_hypothesis_count = openHypothesisCount,
            claim,
            gate_step = "delta_radius < 0",
            gate_done = "radius == 0",
            llm_cue = "Does this conclusion shrink debug-radius? If not — do not promote.",
            reason = shrinks
                ? (done
                    ? "Radius shrank and H is empty — Bug DoD candidate; confirm evidence/VC."
                    : "Step shrinks remaining H — promote allowed.")
                : "Δradius ≥ 0 — do not promote; gather evidence or revise hypothesis."
        }, JsonOptions);
    }

    private static object DescribePack(string knowledgeRepoRoot, string packDir)
    {
        var meta = LlmNativePackReader.TryReadPackMeta(packDir);
        var processes = LlmNativePackReader.TryReadProcesses(packDir);
        var procedures = LlmNativePackReader.TryReadProcedures(packDir);
        var packRel = LlmNativePackReader.ToKnowledgeRelative(knowledgeRepoRoot, packDir);
        return new
        {
            ok = true,
            pack_id = meta?.Id,
            version = meta?.Version,
            title = meta?.Title,
            onboarding = meta?.Onboarding,
            pack_path = packRel,
            definition_ids = LlmNativePackReader.ListDefinitionIds(packDir),
            misconception_ids = LlmNativePackReader.ListMisconceptionIds(packDir),
            process_ids = processes?.Process.Select(p => p.Id).Where(id => id is { Length: > 0 }).ToArray()
                ?? Array.Empty<string>(),
            procedure_ids = procedures?.Procedure.Select(p => p.Id).Where(id => id is { Length: > 0 }).ToArray()
                ?? Array.Empty<string>(),
            sources = meta?.Sources ?? []
        };
    }
}
