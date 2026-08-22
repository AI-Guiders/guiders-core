using Xunit;

namespace AgentFindings.Core.Tests;

public sealed class WorkspaceFindingsStoreTests
{
    [Fact]
    public void Upsert_list_check_roundtrip_hash_gate()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-find-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rel = "src/Foo.cs";
            var full = Path.Combine(root, "src");
            Directory.CreateDirectory(full);
            var file = Path.Combine(full, "Foo.cs");
            File.WriteAllText(file, "class Foo { }");

            var a = WorkspaceFindingsStore.Upsert(
                root, rel, contentHash: null,
                relevance: "on_task", disposition: "touch",
                summary: "entry point", anchors: "Foo",
                dependsOnPaths: null, taskIds: ["t1"],
                status: null, sessionId: "s1");

            Assert.Equal("src/Foo.cs", a.Path);
            Assert.False(string.IsNullOrEmpty(a.ContentHash));
            Assert.Equal("active", a.Status);

            var check = WorkspaceFindingsStore.Check(root, rel, "t1");
            Assert.Equal("reuse_memo", check.Advice);
            Assert.True(check.HashMatch);
            Assert.True(check.DepsOk);

            File.WriteAllText(file, "class Foo { void Bar() {} }");
            var stale = WorkspaceFindingsStore.Check(root, rel, "t1");
            Assert.Equal("reread_file", stale.Advice);
            Assert.False(stale.HashMatch);

            var b = WorkspaceFindingsStore.Upsert(
                root, rel, contentHash: null,
                relevance: "on_task", disposition: "touch",
                summary: "now has Bar", anchors: "Foo,Bar",
                dependsOnPaths: ["src/Bar.cs"], taskIds: ["t1"],
                status: null, sessionId: "s1");

            var latest = WorkspaceFindingsStore.List(root, rel, "t1", null, latestOnly: true, 10);
            Assert.Single(latest);
            Assert.Equal(b.Id, latest[0].Id);
            Assert.Equal("now has Bar", latest[0].Summary);

            var all = WorkspaceFindingsStore.List(root, rel, null, null, latestOnly: false, 10);
            Assert.Equal(2, all.Count);
            Assert.Contains(".agent-findings", WorkspaceFindingsStore.FindingsDir(root), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Check_reports_stale_deps_when_dependency_bytes_change()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-deps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            var foo = Path.Combine(root, "src", "Foo.cs");
            var bar = Path.Combine(root, "src", "Bar.cs");
            File.WriteAllText(foo, "class Foo { }");
            File.WriteAllText(bar, "class Bar { }");

            WorkspaceFindingsStore.Upsert(
                root, "src/Foo.cs", null, "on_task", "touch",
                "depends on Bar", null, ["src/Bar.cs"], ["t1"], null, null);

            var ok = WorkspaceFindingsStore.Check(root, "src/Foo.cs", "t1");
            Assert.Equal("reuse_memo", ok.Advice);
            Assert.True(ok.DepsOk);

            File.WriteAllText(bar, "class Bar { void X() {} }");
            var stale = WorkspaceFindingsStore.Check(root, "src/Foo.cs", "t1");
            Assert.Equal("stale_deps", stale.Advice);
            Assert.True(stale.HashMatch);
            Assert.False(stale.DepsOk);
            Assert.Contains("src/Bar.cs", stale.StaleDeps!);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Task_dag_blocked_becomes_ready_when_blocker_done()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-task-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WorkspaceFindingsStore.UpsertTask(
                root, "A", title: "prep", asIs: "no layer", toBe: "add core", why: "need store",
                blockedBy: null, unlocks: ["B"], memberPaths: ["src/Core.cs"],
                status: "in_progress", sessionId: null);
            WorkspaceFindingsStore.UpsertTask(
                root, "B", title: "wire mcp", asIs: "no tools", toBe: "thin mcp", why: "agent consumer",
                blockedBy: ["A"], unlocks: null, memberPaths: ["src/Program.cs"],
                status: "pending", sessionId: null);

            var b1 = WorkspaceFindingsStore.GetTask(root, "B");
            Assert.NotNull(b1);
            Assert.Equal("blocked", b1!.EffectiveStatus);
            Assert.Contains("A", b1.WaitingOn!);

            WorkspaceFindingsStore.UpsertTask(
                root, "A", title: "prep", asIs: "core exists", toBe: "done", why: "shipped",
                blockedBy: null, unlocks: ["B"], memberPaths: ["src/Core.cs"],
                status: "done", sessionId: null);

            var b2 = WorkspaceFindingsStore.GetTask(root, "B");
            Assert.Equal("ready", b2!.EffectiveStatus);
            Assert.Null(b2.WaitingOn);

            var ready = WorkspaceFindingsStore.ListTasks(root, null, "ready", latestOnly: true, 10);
            Assert.Contains(ready, v => v.Task.TaskId == "B");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void UpsertTask_partial_keeps_prior_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WorkspaceFindingsStore.UpsertTask(
                root, "T", title: "keep me", asIs: "old", toBe: "new", why: "because",
                blockedBy: null, unlocks: null, memberPaths: ["a.cs"],
                status: "in_progress", sessionId: null);

            WorkspaceFindingsStore.UpsertTask(
                root, "T", title: null, asIs: null, toBe: null, why: null,
                blockedBy: null, unlocks: null, memberPaths: null,
                status: "done", sessionId: null);

            var t = WorkspaceFindingsStore.GetTask(root, "T")!.Task;
            Assert.Equal("done", t.Status);
            Assert.Equal("keep me", t.Title);
            Assert.Equal("old", t.AsIs);
            Assert.Equal("new", t.ToBe);
            Assert.Equal("because", t.Why);
            Assert.Contains("a.cs", t.MemberPaths!);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Concurrent_upserts_do_not_lose_lines()
    {
        var root = Path.Combine(Path.GetTempPath(), "af-find-par-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "Foo.cs"), "class Foo { }");

            const int n = 48;
            Parallel.For(0, n, i =>
            {
                WorkspaceFindingsStore.Upsert(
                    root, "src/Foo.cs", contentHash: null,
                    relevance: "on_task", disposition: "touch",
                    summary: $"parallel-{i}", anchors: null,
                    dependsOnPaths: null, taskIds: ["par"],
                    status: null, sessionId: $"s{i}");
            });

            var lines = File.ReadAllLines(
                Path.Combine(root, WorkspaceFindingsStore.DefaultRelativeDir, WorkspaceFindingsStore.MemosFileName));
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
        var prev = Environment.GetEnvironmentVariable(WorkspaceFindingsStore.RelativeDirEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(WorkspaceFindingsStore.RelativeDirEnvVar, ".custom-findings");
            Assert.Equal(".custom-findings", WorkspaceFindingsStore.RelativeDirName());
            var root = Path.Combine(Path.GetTempPath(), "af-fenv-" + Guid.NewGuid().ToString("N"));
            Assert.EndsWith(".custom-findings", WorkspaceFindingsStore.FindingsDir(root), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkspaceFindingsStore.RelativeDirEnvVar, prev);
        }
    }

    [Fact]
    public void NormalizePath_rejects_parent_segments()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceFindingsStore.NormalizePath("../x"));
    }
}
