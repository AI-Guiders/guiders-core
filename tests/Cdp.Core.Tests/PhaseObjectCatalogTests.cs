using Cdp.Core;
using Xunit;

namespace Cdp.Core.Tests;

public class PhaseObjectCatalogTests
{
    private static readonly IReadOnlyList<ToolAffordance> Seed = Wave1AffordanceSeed.Build();

    [Fact]
    public void Explore_Kb_Includes_World_KnowledgeTags()
    {
        var hits = PhaseObjectCatalog.Query(Seed, CdpPhase.Explore, CdpObjectKind.Kb, CdpIntent.Cite);
        Assert.Contains(hits, h => h.Affordance.PrefixedName == "memory_world_knowledge_tags");
        Assert.DoesNotContain(hits, h => h.Affordance.PrefixedName == "memory_world_write_knowledge_file");
    }

    [Fact]
    public void Recall_Kb_Excludes_ListFiles_Includes_Pack()
    {
        var hits = PhaseObjectCatalog.Query(Seed, CdpPhase.Recall, CdpObjectKind.Kb, CdpIntent.Cite);
        Assert.Contains(hits, h => h.Affordance.PrefixedName == "memory_world_get_definition");
        Assert.Contains(hits, h => h.Affordance.PrefixedName == "memory_world_knowledge_tags");
        Assert.DoesNotContain(hits, h => h.Affordance.UnderlyingName == "list_knowledge_files");
        Assert.DoesNotContain(hits, h => h.Affordance.PrefixedName == "memory_project_get_definition");
        Assert.True(hits.Count <= PhaseObjectCatalog.DefaultListToolsLimit);
    }

    [Fact]
    public void Explore_Kb_Can_ListFiles()
    {
        var hits = PhaseObjectCatalog.Query(
            Seed, CdpPhase.Explore, CdpObjectKind.Kb, CdpIntent.Find, limit: PhaseObjectCatalog.MaxQueryLimit);
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "list_knowledge_files");
    }

    [Fact]
    public void Explore_Kb_Includes_Pack_GetDefinition_World_Only()
    {
        var hits = PhaseObjectCatalog.Query(Seed, CdpPhase.Explore, CdpObjectKind.Kb, CdpIntent.Cite);
        Assert.Contains(hits, h => h.Affordance.PrefixedName == "memory_world_get_definition");
        Assert.DoesNotContain(hits, h => h.Affordance.PrefixedName == "memory_project_get_definition");
        Assert.DoesNotContain(hits, h => h.Affordance.PrefixedName == "memory_skill_get_definition");
    }

    [Fact]
    public void Explore_Kb_ListTools_Budget_Is_Tight()
    {
        var hits = PhaseObjectCatalog.Query(Seed, CdpPhase.Explore, CdpObjectKind.Kb, CdpIntent.Cite);
        Assert.True(hits.Count <= PhaseObjectCatalog.DefaultListToolsLimit);
        var underlyings = hits.Select(h => h.Affordance.UnderlyingName).ToArray();
        Assert.Equal(underlyings.Length, underlyings.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Explore_Kb_Includes_RadiusGate()
    {
        var hits = PhaseObjectCatalog.Query(Seed, CdpPhase.Explore, CdpObjectKind.Kb, CdpIntent.Cite);
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "radius_gate_check");
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "get_process");
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "get_procedure");
    }

    [Fact]
    public void Act_Task_Includes_TaskUpsert()
    {
        var hits = PhaseObjectCatalog.Query(Seed, CdpPhase.Act, CdpObjectKind.Task, CdpIntent.Change);
        Assert.Contains(hits, h => h.Affordance.PrefixedName == "memory_task_task_upsert");
    }

    [Fact]
    public void Intent_Cite_Ranks_Tags_Above_ListFiles()
    {
        var hits = PhaseObjectCatalog.Query(
            Seed, CdpPhase.Explore, CdpObjectKind.Kb, CdpIntent.Cite, limit: PhaseObjectCatalog.MaxQueryLimit);
        var tags = hits.First(h => h.Affordance.PrefixedName == "memory_world_knowledge_tags");
        var list = hits.FirstOrDefault(h => h.Affordance.PrefixedName == "memory_world_list_knowledge_files");
        if (list is not null)
            Assert.True(tags.Score >= list.Score);
    }

    [Fact]
    public void ColdStart_Is_Recall_Not_Browse()
    {
        var cold = PhaseObjectCatalog.DefaultColdStart(Seed);
        Assert.InRange(cold.Count, 1, PhaseObjectCatalog.DefaultListToolsLimit);
        Assert.All(cold, a => Assert.Contains(CdpObjectKind.Kb, a.Objects));
        Assert.Contains(cold, a => a.PrefixedName == "memory_world_get_definition");
        Assert.DoesNotContain(cold, a => a.UnderlyingName == "list_knowledge_files");
        Assert.All(cold, a => Assert.Contains(CdpPhase.Recall, a.Phases));
    }

    [Fact]
    public void Language_Python_Hides_Csharp_Only_Debug()
    {
        var hits = PhaseObjectCatalog.Query(
            Seed,
            CdpPhase.Act,
            CdpObjectKind.Code,
            CdpIntent.Change,
            limit: PhaseObjectCatalog.MaxQueryLimit,
            language: CdpLanguages.Python);
        Assert.DoesNotContain(hits, h => h.Affordance.PrefixedName == "debug_launch");
        Assert.DoesNotContain(hits, h => h.Affordance.Domain == CdpDomains.Debug);
    }

    [Fact]
    public void Language_Csharp_Keeps_Debug_And_Boosts()
    {
        var withCs = PhaseObjectCatalog.Query(
            Seed, CdpPhase.Act, CdpObjectKind.Process, CdpIntent.Change, limit: 20, language: CdpLanguages.Csharp);
        var launch = Assert.Single(withCs, h => h.Affordance.PrefixedName == "debug_launch");
        var noLang = PhaseObjectCatalog.Query(
            Seed, CdpPhase.Act, CdpObjectKind.Process, CdpIntent.Change, limit: 20);
        var launchAny = Assert.Single(noLang, h => h.Affordance.PrefixedName == "debug_launch");
        Assert.True(launch.Score > launchAny.Score);
    }

    [Fact]
    public void Split_LongestPrefix_SelfFinding()
    {
        Assert.True(CdpDomains.TrySplit("memory_self_finding_findings", out var d, out var u));
        Assert.Equal(CdpDomains.MemorySelfFinding, d);
        Assert.Equal("findings", u);
    }

    [Fact]
    public void LayerOf_Memory_And_Dev()
    {
        Assert.Equal(CdpLayer.Memory, CdpDomains.LayerOf(CdpDomains.MemoryWorld));
        Assert.Equal(CdpLayer.Dev, CdpDomains.LayerOf(CdpDomains.Build));
        Assert.Equal(CdpLayer.Dev, CdpDomains.LayerOf(CdpDomains.Roslyn));
        Assert.Equal(CdpLayer.Dev, CdpDomains.LayerOf(CdpDomains.Git));
        Assert.Equal(CdpLayer.Dev, CdpDomains.LayerOf(CdpDomains.CodebaseIndex));
        Assert.Equal(CdpLayer.Dev, CdpDomains.LayerOf(CdpDomains.Anui));
    }

    [Fact]
    public void Split_Roslyn_DoublePrefix()
    {
        Assert.True(CdpDomains.TrySplit("roslyn_roslyn_get_diagnostics", out var d, out var u));
        Assert.Equal(CdpDomains.Roslyn, d);
        Assert.Equal("roslyn_get_diagnostics", u);
    }

    [Fact]
    public void Split_CodebaseIndex_DoublePrefix()
    {
        Assert.True(CdpDomains.TrySplit("codebase_index_codebase_index_search", out var d, out var u));
        Assert.Equal(CdpDomains.CodebaseIndex, d);
        Assert.Equal("codebase_index_search", u);
    }

    [Fact]
    public void Split_Git_DoublePrefix()
    {
        Assert.True(CdpDomains.TrySplit("git_git_status", out var d, out var u));
        Assert.Equal(CdpDomains.Git, d);
        Assert.Equal("git_status", u);
    }

    [Fact]
    public void ParsePhase_Recall()
    {
        Assert.True(CdpEnumParse.TryParsePhase("recall", out var p));
        Assert.Equal(CdpPhase.Recall, p);
        Assert.Equal("recall", CdpEnumParse.ToWire(CdpPhase.Recall));
    }

    [Fact]
    public void ParsePhase_Plan()
    {
        Assert.True(CdpEnumParse.TryParsePhase("plan", out var p));
        Assert.Equal(CdpPhase.Plan, p);
        Assert.Equal("plan", CdpEnumParse.ToWire(CdpPhase.Plan));
    }

    [Fact]
    public void ParsePhase_Review()
    {
        Assert.True(CdpEnumParse.TryParsePhase("review", out var p));
        Assert.Equal(CdpPhase.Review, p);
        Assert.Equal("review", CdpEnumParse.ToWire(CdpPhase.Review));
    }

    [Fact]
    public void Act_Kb_Change_Keeps_World_And_Project_Append()
    {
        var hits = PhaseObjectCatalog.Query(
            Seed, CdpPhase.Act, CdpObjectKind.Kb, CdpIntent.Change, limit: PhaseObjectCatalog.MaxQueryLimit);
        Assert.Contains(hits, h => h.Affordance.PrefixedName == "memory_world_append_knowledge_file");
        Assert.Contains(hits, h => h.Affordance.PrefixedName == "memory_project_append_knowledge_file");
        var projectAppend = hits.First(h => h.Affordance.PrefixedName == "memory_project_append_knowledge_file");
        Assert.Contains("kolb JOURNAL", projectAppend.Affordance.Hint);
    }

    [Fact]
    public void Plan_Kb_Includes_Pack_Scouts_Excludes_Writes()
    {
        var hits = PhaseObjectCatalog.Query(Seed, CdpPhase.Plan, CdpObjectKind.Kb, CdpIntent.Find);
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "get_procedure");
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "get_definition");
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "list_knowledge_files");
        Assert.DoesNotContain(hits, h => h.Affordance.UnderlyingName == "write_knowledge_file");
    }

    [Fact]
    public void Explore_Code_Find_Prefers_Hci_Search_Over_Legacy_Roslyn()
    {
        var hits = PhaseObjectCatalog.Query(
            Seed,
            CdpPhase.Explore,
            CdpObjectKind.Code,
            CdpIntent.Find,
            limit: PhaseObjectCatalog.DefaultListToolsLimit,
            language: CdpLanguages.Csharp);

        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "codebase_index_search");
        var search = hits.First(h => h.Affordance.UnderlyingName == "codebase_index_search");
        var legacyGo = hits.FirstOrDefault(h => h.Affordance.UnderlyingName == "roslyn_go_to_definition");
        if (legacyGo is not null)
            Assert.True(search.Score > legacyGo.Score);

        Assert.DoesNotContain(hits, h => h.Affordance.Domain == CdpDomains.Debug);
    }

    [Fact]
    public void Explore_Code_Includes_Git_Scene()
    {
        var hits = PhaseObjectCatalog.Query(
            Seed,
            CdpPhase.Explore,
            CdpObjectKind.Code,
            CdpIntent.Find,
            limit: PhaseObjectCatalog.MaxQueryLimit);
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "git_scene");
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "git_diff_scene");
    }

    [Fact]
    public void Act_Code_Ship_Includes_Git_Commit()
    {
        var hits = PhaseObjectCatalog.Query(
            Seed,
            CdpPhase.Act,
            CdpObjectKind.Code,
            CdpIntent.Ship,
            limit: PhaseObjectCatalog.MaxQueryLimit);
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "git_commit");
        Assert.Contains(hits, h => h.Affordance.UnderlyingName == "git_push");
    }
}
