using TerminalMcp.Core;
using Xunit;

namespace TerminalMcp.Core.Tests;

public sealed class DurableJobStoreTests : IDisposable
{
    readonly string _root;

    public DurableJobStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "durable-jobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DurableJobStore.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        DurableJobStore.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Enqueue_claim_finish_roundtrip()
    {
        var started = DurableJobStore.EnqueueShell(
            command: "echo hi",
            argv: null,
            tab: "fremus",
            cwd: null,
            shell: null,
            timeoutSeconds: null,
            codepage: null,
            armId: "terminal-shell-bg-fremus");

        Assert.Contains("job_id", started, StringComparison.Ordinal);
        using var doc = System.Text.Json.JsonDocument.Parse(started);
        var jobId = doc.RootElement.GetProperty("job_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        var claimed = DurableJobStore.TryClaimNext("test-supervisor");
        Assert.NotNull(claimed);
        Assert.Equal(jobId, claimed!.JobId);
        Assert.Equal("running", claimed.State);

        DurableJobStore.Finish(jobId!, ok: true, resultJson: """{"ok":true,"exit_code":0}""");
        var last = DurableJobStore.Last(jobId, kind: null);
        Assert.Contains("\"ok\":true", last, StringComparison.Ordinal);
    }
}
