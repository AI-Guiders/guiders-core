using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Cdp.Evidence;
using DotNetBuildTestParsers;

namespace DotNetBuildTest.Core;

/// <summary>Очередь build/test/publish jobs с cancel, log и structured JSON results.</summary>
public sealed class BuildTestJobCoordinator
{
    private const int QueueCapacity = 8;
    private const int DefaultRetryAfterSeconds = 3;
    private const int MaxStoredLogLines = 10000;
    private readonly Channel<BuildTestJobEnvelope> _queue = Channel.CreateBounded<BuildTestJobEnvelope>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    private readonly ConcurrentDictionary<string, BuildTestJobEnvelope> _jobs = new();
    private int _queuedCount;

    public BuildTestJobCoordinator()
    {
        _ = Task.Run(ProcessQueueAsync);
    }

    public BuildTestEnqueueResult TryEnqueue(
        BuildTestJobKind kind,
        string solutionPath,
        bool includeRawOutput,
        string detail,
        int timeoutSeconds,
        DotnetExecutionOptions dotnetOptions)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var envelope = new BuildTestJobEnvelope(
            jobId, kind, solutionPath, includeRawOutput, detail, timeoutSeconds, dotnetOptions);
        _jobs[jobId] = envelope;

        if (!_queue.Writer.TryWrite(envelope))
        {
            _jobs.TryRemove(jobId, out _);
            return new BuildTestEnqueueResult(false, null, DefaultRetryAfterSeconds);
        }

        Interlocked.Increment(ref _queuedCount);
        return new BuildTestEnqueueResult(true, jobId, DefaultRetryAfterSeconds);
    }

    public async Task<string?> WaitForCompletionAsync(string jobId, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var envelope))
            return null;

        try
        {
            return await envelope.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public object? GetJobStatus(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var envelope))
            return null;

        return new
        {
            found = true,
            job_id = envelope.Id,
            tool = envelope.Kind switch
            {
                BuildTestJobKind.BuildStructured => "build_structured",
                BuildTestJobKind.RunTests => "run_tests",
                BuildTestJobKind.PublishStructured => "publish_structured",
                _ => "unknown"
            },
            dotnet_options = envelope.DotnetOptions.ToStatusSnapshot(),
            status = envelope.State.ToString().ToLowerInvariant(),
            created_at_utc = envelope.CreatedAtUtc,
            started_at_utc = envelope.StartedAtUtc,
            completed_at_utc = envelope.CompletedAtUtc,
            timeout_seconds = envelope.TimeoutSeconds,
            cancel_requested = envelope.CancelRequested,
            queue_depth = Math.Max(Interlocked.CompareExchange(ref _queuedCount, 0, 0), 0),
            log_lines = envelope.LogLineCount,
            result = envelope.ResultJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(envelope.ResultJson)
        };
    }

    public object? GetJobLogChunk(string jobId, int offset, int limit)
    {
        if (!_jobs.TryGetValue(jobId, out var envelope))
            return null;

        var all = envelope.LogLines.ToArray();
        var start = Math.Clamp(offset, 0, all.Length);
        var count = Math.Clamp(limit, 0, all.Length - start);
        var chunk = all.Skip(start).Take(count).ToArray();
        var nextOffset = start + count;

        return new
        {
            found = true,
            job_id = jobId,
            offset_lines = start,
            returned_lines = chunk.Length,
            next_offset_lines = nextOffset,
            has_more = nextOffset < all.Length,
            lines = chunk
        };
    }

    public object CancelJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var envelope))
            return new { found = false, job_id = jobId, cancelled = false, message = "Job not found." };

        envelope.CancelRequested = true;
        envelope.RuntimeCancellation?.Cancel();

        return new
        {
            found = true,
            job_id = envelope.Id,
            cancelled = true,
            status = envelope.State.ToString().ToLowerInvariant()
        };
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _queuedCount);

            if (job.CancelRequested)
            {
                MarkCancelled(job, "Cancelled before execution.");
                continue;
            }

            job.State = BuildTestJobState.Running;
            job.StartedAtUtc = DateTimeOffset.UtcNow;
            using var runtimeCts = new CancellationTokenSource();
            job.RuntimeCancellation = runtimeCts;

            try
            {
                var resultJson = job.Kind switch
                {
                    BuildTestJobKind.BuildStructured => await ExecuteBuildAsync(job, runtimeCts.Token).ConfigureAwait(false),
                    BuildTestJobKind.RunTests => await ExecuteTestsAsync(job, runtimeCts.Token).ConfigureAwait(false),
                    BuildTestJobKind.PublishStructured => await ExecutePublishAsync(job, runtimeCts.Token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Unknown job kind.")
                };

                job.ResultJson = resultJson;
                var parsed = JsonSerializer.Deserialize<JsonElement>(resultJson);
                var success = parsed.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                job.State = success ? BuildTestJobState.Done : BuildTestJobState.Failed;
                job.CompletedAtUtc = DateTimeOffset.UtcNow;
                job.Completion.TrySetResult(resultJson);
            }
            catch (OperationCanceledException)
            {
                MarkCancelled(job, "Cancelled by request.");
            }
            catch (Exception ex)
            {
                var errorResult = BuildTestJson.Serialize(new
                {
                    success = false,
                    error = ex.Message,
                    job_id = job.Id,
                    status = "failed"
                });
                job.ResultJson = errorResult;
                job.State = BuildTestJobState.Failed;
                job.CompletedAtUtc = DateTimeOffset.UtcNow;
                job.Completion.TrySetResult(errorResult);
            }
            finally
            {
                job.RuntimeCancellation = null;
            }
        }
    }

    private static void MarkCancelled(BuildTestJobEnvelope job, string reason)
    {
        var state = job.CancelRequested ? BuildTestJobState.Cancelled : BuildTestJobState.TimedOut;
        var result = BuildTestJson.Serialize(new
        {
            success = false,
            job_id = job.Id,
            status = state.ToString().ToLowerInvariant(),
            reason
        });
        job.ResultJson = result;
        job.State = state;
        job.CompletedAtUtc = DateTimeOffset.UtcNow;
        job.Completion.TrySetResult(result);
    }

    private async Task<string> ExecuteBuildAsync(BuildTestJobEnvelope job, CancellationToken cancellationToken)
    {
        var workingDir = Path.GetDirectoryName(job.SolutionPath) ?? "";
        var run = await DotnetProcessRunner.RunAsync(
            workingDir,
            DotnetCommandBuilder.BuildBuildArgs(job.SolutionPath, job.DotnetOptions),
            job.TimeoutSeconds,
            cancellationToken,
            line => AddLogLine(job, line),
            job.DotnetOptions.SupplementalEnvironmentVariables).ConfigureAwait(false);

        return SerializeBuildLikeResult(job, workingDir, run, EvidenceSource.Build);
    }

    private async Task<string> ExecuteTestsAsync(BuildTestJobEnvelope job, CancellationToken cancellationToken)
    {
        var workingDir = Path.GetDirectoryName(job.SolutionPath) ?? "";
        var run = await DotnetProcessRunner.RunAsync(
            workingDir,
            DotnetCommandBuilder.BuildTestArgs(job.SolutionPath, job.DotnetOptions),
            job.TimeoutSeconds,
            cancellationToken,
            line => AddLogLine(job, line),
            job.DotnetOptions.SupplementalEnvironmentVariables).ConfigureAwait(false);

        var parsed = TestOutputParser.Parse(run.Output);
        var success = parsed.Success && !run.TimedOut && !run.Cancelled;
        var eff = BuildTestResultDetail.Effective(job.Detail, success);
        var wantEvidence = eff is BuildTestResultDetail.Slim or BuildTestResultDetail.Full;
        object? evidenceDto = null;
        if (wantEvidence)
        {
            var ctx = new EvidenceContext(
                ProjectRoot: workingDir,
                SolutionOrProjectPath: job.SolutionPath,
                IncludeWarnings: job.IncludeRawOutput || eff == BuildTestResultDetail.Full,
                MaxItems: job.IncludeRawOutput || eff == BuildTestResultDetail.Full ? 80 : 24);
            var evidence = EvidencePreprocess.FromFailedTests(
                parsed.FailedTests.Select(t => (t.Name, t.Message)),
                ctx,
                rawOutput: job.IncludeRawOutput || parsed.Failed > 0 ? run.Output : null);
            evidenceDto = EvidencePreprocess.ToDto(evidence);
        }

        // SoftFL: Total=0 must not read as green ok (lived: session CdpMcp.csproj + filter → test ok 0/0).
        var pulse = parsed.Empty
            ? FormatTestEmptyPulse(job)
            : parsed.Success
                ? $"test ok {parsed.Passed}/{parsed.Total}"
                : $"test fail F×{parsed.Failed} {parsed.Passed}/{parsed.Total}";
        var status = run.TimedOut ? "timed_out" : run.Cancelled ? "cancelled" : "completed";
        var durationMs = (int)(DateTimeOffset.UtcNow - (job.StartedAtUtc ?? DateTimeOffset.UtcNow)).TotalMilliseconds;
        var result = BuildTestResultDetail.ShapeTest(
            job.Detail,
            success,
            pulse,
            parsed,
            evidenceDto,
            job.DotnetOptions.Filter,
            job.Id,
            status,
            run.TimedOut,
            run.Cancelled,
            run.FailureReason,
            durationMs,
            run.Output);

        TestRunCache.Remember(
            job.SolutionPath,
            success,
            parsed.Total,
            parsed.Passed,
            parsed.Failed,
            parsed.Skipped,
            parsed.FailedTests.Select(t => (t.Name, t.Message, t.DurationMs)),
            job.DotnetOptions.Filter);

        if (run.TimedOut)
            job.CancelRequested = false;

        return BuildTestJson.Serialize(result);
    }

    static string FormatTestEmptyPulse(BuildTestJobEnvelope job)
    {
        var filter = job.DotnetOptions.Filter;
        var tip = string.IsNullOrWhiteSpace(filter)
            ? "tip path=*.Tests.csproj"
            : $"filter={filter.Trim()} · tip path=*.Tests.csproj";
        return $"test empty 0/0 · {tip}";
    }

    private async Task<string> ExecutePublishAsync(BuildTestJobEnvelope job, CancellationToken cancellationToken)
    {
        var workingDir = Path.GetDirectoryName(job.SolutionPath) ?? "";
        var run = await DotnetProcessRunner.RunAsync(
            workingDir,
            DotnetCommandBuilder.BuildPublishArgs(job.SolutionPath, job.DotnetOptions),
            job.TimeoutSeconds,
            cancellationToken,
            line => AddLogLine(job, line),
            job.DotnetOptions.SupplementalEnvironmentVariables).ConfigureAwait(false);

        return SerializeBuildLikeResult(job, workingDir, run, EvidenceSource.Publish);
    }

    private string SerializeBuildLikeResult(
        BuildTestJobEnvelope job,
        string workingDir,
        CommandExecutionResult run,
        EvidenceSource evidenceSource)
    {
        var parseInput = run.Output + Environment.NewLine + $"Exit code: {run.ExitCode}";
        var parsed = BuildOutputParser.Parse(parseInput);
        var success = parsed.Success && !run.TimedOut && !run.Cancelled;
        var eff = BuildTestResultDetail.Effective(job.Detail, success);
        var wantEvidence = eff is BuildTestResultDetail.Slim or BuildTestResultDetail.Full;
        object? evidenceDto = null;
        if (wantEvidence)
        {
            var ctx = new EvidenceContext(
                ProjectRoot: workingDir,
                SolutionOrProjectPath: job.SolutionPath,
                IncludeWarnings: job.IncludeRawOutput || eff == BuildTestResultDetail.Full,
                MaxItems: job.IncludeRawOutput || eff == BuildTestResultDetail.Full ? 80 : 24);
            var evidence = EvidencePreprocess.FromBuildDiagnostics(
                parsed.Errors.Select(e => (e.File, e.Line, e.Column, e.Code, e.Message, IsError: true))
                    .Concat(job.IncludeRawOutput || eff == BuildTestResultDetail.Full
                        ? parsed.Warnings.Select(w => (w.File, w.Line, w.Column, w.Code, w.Message, IsError: false))
                        : parsed.Warnings.Take(3).Select(w => (w.File, w.Line, w.Column, w.Code, w.Message, IsError: false))),
                ctx,
                evidenceSource);
            evidenceDto = EvidencePreprocess.ToDto(evidence);
        }

        var pulse = parsed.Success
            ? $"build ok E×0 W×{parsed.Warnings.Count}"
            : $"build fail E×{parsed.Errors.Count} W×{parsed.Warnings.Count}";
        var status = run.TimedOut ? "timed_out" : run.Cancelled ? "cancelled" : "completed";
        var durationMs = (int)(DateTimeOffset.UtcNow - (job.StartedAtUtc ?? DateTimeOffset.UtcNow)).TotalMilliseconds;
        var result = BuildTestResultDetail.ShapeBuild(
            job.Detail,
            success,
            pulse,
            parsed,
            evidenceDto,
            job.Id,
            status,
            run.TimedOut,
            run.Cancelled,
            run.FailureReason,
            durationMs,
            run.Output,
            includeWarningsFull: job.IncludeRawOutput || eff == BuildTestResultDetail.Full);

        if (run.TimedOut)
            job.CancelRequested = false;

        return BuildTestJson.Serialize(result);
    }

    private static void AddLogLine(BuildTestJobEnvelope job, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        job.LogLines.Enqueue(line);
        var current = Interlocked.Increment(ref job.LogLineCount);
        while (current > MaxStoredLogLines && job.LogLines.TryDequeue(out _))
            current = Interlocked.Decrement(ref job.LogLineCount);
    }
}

internal sealed class BuildTestJobEnvelope
{
    public BuildTestJobEnvelope(
        string id,
        BuildTestJobKind kind,
        string solutionPath,
        bool includeRawOutput,
        string detail,
        int timeoutSeconds,
        DotnetExecutionOptions dotnetOptions)
    {
        Id = id;
        Kind = kind;
        SolutionPath = solutionPath;
        IncludeRawOutput = includeRawOutput;
        Detail = detail;
        TimeoutSeconds = timeoutSeconds;
        DotnetOptions = dotnetOptions;
    }

    public string Id { get; }
    public BuildTestJobKind Kind { get; }
    public string SolutionPath { get; }
    public bool IncludeRawOutput { get; }
    public string Detail { get; }
    public int TimeoutSeconds { get; }
    public DotnetExecutionOptions DotnetOptions { get; }
    public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public BuildTestJobState State { get; set; } = BuildTestJobState.Queued;
    public bool CancelRequested { get; set; }
    public CancellationTokenSource? RuntimeCancellation { get; set; }
    public ConcurrentQueue<string> LogLines { get; } = new();
    public int LogLineCount;
    public string? ResultJson { get; set; }
    public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
