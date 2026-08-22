using Xunit;

namespace AgentFailures.Core.Tests;

public sealed class WorkspaceFailuresStoreTests
{
    [Fact]
    public void Append_and_list_roundtrip_increments_seenCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var a = WorkspaceFailuresStore.Append(
                root, "codebase_index_search", "query_miss",
                """{"query":"x"}""", "relax filters", null, "AND too strict", null, null);
            // Outside dedupe: force by using different wall? Same window — second append without patch dedupes.
            // Use resolution-less but wait — within 15m dedupes. Bump via fingerprint override path:
            // First record then second with argsTried counts as meta patch → new line.
            var b = WorkspaceFailuresStore.Record(
                root, "codebase_index_search", "query_miss",
                """{"query":"y"}""", null, null, null, null, null,
                category: "incorrect_invocation", projectId: null, app: null, suggestedNext: null);

            Assert.False(b.Deduped);
            Assert.Equal(a.Fingerprint, b.Record.Fingerprint);
            Assert.Equal(1, a.SeenCount);
            Assert.Equal(2, b.Record.SeenCount);
            Assert.Equal(a.Id, b.Record.SeenBefore);
            Assert.Equal("incorrect_invocation", b.Record.Category);

            var list = WorkspaceFailuresStore.List(root, "codebase_index_search", null, 10);
            Assert.Equal(2, list.Count);
            Assert.True(File.Exists(WorkspaceFailuresStore.FileForTool(root, "codebase_index_search")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Dedupe_same_fingerprint_within_window()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-dedup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var a = WorkspaceFailuresStore.Record(
                root, "aid-publish", "file locked dll",
                null, null, null, null, null, null,
                "environment", "ai-native-ui", "AnuiAgentMcp", null);
            var b = WorkspaceFailuresStore.Record(
                root, "aid-publish", "file locked dll",
                null, null, null, null, null, null,
                null, null, null, null);

            Assert.False(a.Deduped);
            Assert.True(b.Deduped);
            Assert.Equal(a.Record.Id, b.Record.Id);
            Assert.Contains("KillRunning", b.SuggestedNext ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Resolution_upsert_merges_without_inflating_seen()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-res-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var a = WorkspaceFailuresStore.Record(
                root, "aid-publish", "file locked",
                "-KillRunning wrong name", null, null, null, null, "anui-cide-visual",
                "environment", "ai-native-ui", "AnuiAgentMcp", null);
            var b = WorkspaceFailuresStore.Record(
                root, "aid-publish", null,
                null, "use -AppExeName AnuiAgentMcp", "-AppExeName AnuiAgentMcp -KillRunning",
                "AssemblyName mismatch", a.Record.Fingerprint, null,
                null, null, null, null);

            Assert.False(b.Deduped);
            Assert.Equal(1, b.Record.SeenCount);
            Assert.Equal(a.Record.Id, b.Record.SeenBefore);
            Assert.Equal("environment", b.Record.Category);
            Assert.Equal("ai-native-ui", b.Record.ProjectId);
            Assert.Equal("AnuiAgentMcp", b.Record.App);
            Assert.Contains("AppExeName", b.Record.CorrectArgs!);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void List_filters_by_category_and_project()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-filt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WorkspaceFailuresStore.Record(
                root, "t1", "bad args", null, null, null, null, null, null,
                "incorrect_invocation", "cascade-ide", null, null);
            WorkspaceFailuresStore.Record(
                root, "t2", "locked", null, null, null, null, null, null,
                "environment", "ai-native-ui", "AnuiAgentMcp", null);

            var env = WorkspaceFailuresStore.List(
                root, null, null, "environment", "ai-native-ui", null, null, latestOnly: true, 10);
            Assert.Single(env);
            Assert.Equal("t2", env[0].Record.Tool);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Concurrent_records_do_not_lose_lines()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-fail-par-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const int n = 48;
            Parallel.For(0, n, i =>
            {
                WorkspaceFailuresStore.Record(
                    root, "parallel_tool", $"miss-{i}", null, null, null, null,
                    fingerprint: $"fp-{i}", taskId: null,
                    category: "unknown", projectId: null, app: null, suggestedNext: null);
            });

            var lines = File.ReadAllLines(WorkspaceFailuresStore.FileForTool(root, "parallel_tool"));
            Assert.Equal(n, lines.Count(l => !string.IsNullOrWhiteSpace(l)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void RelativeDir_env_override()
    {
        var prev = Environment.GetEnvironmentVariable(WorkspaceFailuresStore.RelativeDirEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(WorkspaceFailuresStore.RelativeDirEnvVar, ".custom-failures");
            Assert.Equal(".custom-failures", WorkspaceFailuresStore.RelativeDirName());
            var root = Path.Combine(Path.GetTempPath(), "af-env-" + Guid.NewGuid().ToString("N"));
            Assert.EndsWith(".custom-failures", WorkspaceFailuresStore.FailuresDir(root), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkspaceFailuresStore.RelativeDirEnvVar, prev);
        }
    }
}
