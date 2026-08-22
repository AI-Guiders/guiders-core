using System.Text.Json;

namespace DotNetBuildTest.Core;

/// <summary>Фасад для MCP и IDE: enqueue build/test/publish и job control.</summary>
public sealed class BuildTestJobService
{
    private readonly BuildTestJobCoordinator _coordinator;

    public BuildTestJobService()
        : this(new BuildTestJobCoordinator())
    {
    }

    public BuildTestJobService(BuildTestJobCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public BuildTestJobCoordinator Coordinator => _coordinator;

    public async Task<string> BuildStructuredAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken) =>
        await EnqueueToolAsync(
            BuildTestJobKind.BuildStructured,
            args,
            BuildTestToolRequestParser.DefaultBuildTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

    public async Task<string> RunTestsAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken) =>
        await EnqueueToolAsync(
            BuildTestJobKind.RunTests,
            args,
            BuildTestToolRequestParser.DefaultTestTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Discover FQNs + last run — prefer before shell / blind cdp_test.</summary>
    public Task<string> TestSceneAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var request = BuildTestToolRequestParser.ParseExecutionRequest(
            args, BuildTestToolRequestParser.DefaultTestTimeoutSeconds);
        var max = BuildTestToolRequestParser.TryGetInt(args, "max_tests", out var mt)
            ? mt : TestListParser.MaxDefault;
        return TestScene.SceneAsync(
            request.SolutionPath,
            request.DotnetOptions,
            max,
            request.TimeoutSeconds,
            cancellationToken);
    }

    /// <summary>Preview or apply a test selection (include / failed_first / filter).</summary>
    public async Task<string> TestPlanAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var op = BuildTestToolRequestParser.TryGetString(args, "op", out var opRaw) && !string.IsNullOrWhiteSpace(opRaw)
            ? opRaw!.Trim().ToLowerInvariant()
            : "preview";

        var request = BuildTestToolRequestParser.ParseExecutionRequest(
            args, BuildTestToolRequestParser.DefaultTestTimeoutSeconds);
        var include = TestScene.ParseInclude(args);
        var failedFirst = args.TryGetValue("failed_first", out var ff) && ff.ValueKind == JsonValueKind.True;
        BuildTestToolRequestParser.TryGetString(args, "filter", out var rawFilter);

        if (op is "preview" or "draft")
            return TestScene.PlanPreview(request.SolutionPath, include, failedFirst, rawFilter);

        if (op is not "apply" and not "run")
            throw new ArgumentException("op must be preview|apply (aliases draft|run).");

        // Merge selection into filter for run_tests.
        string? filter = null;
        if (!string.IsNullOrWhiteSpace(rawFilter))
            filter = rawFilter!.Trim();
        else if (failedFirst)
            filter = TestPlanFilter.FromFailedFirst(TestRunCache.TryGet(
                SolutionOrProjectPathResolver.Resolve(request.SolutionPath)));
        else if (include.Count > 0)
            filter = TestPlanFilter.FromIncludes(include);
        else if (!string.IsNullOrWhiteSpace(request.DotnetOptions.Filter))
            filter = request.DotnetOptions.Filter;

        var merged = new Dictionary<string, JsonElement>(args, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(filter))
            merged["filter"] = JsonSerializer.SerializeToElement(filter);

        var runJson = await EnqueueToolAsync(
            BuildTestJobKind.RunTests,
            merged,
            BuildTestToolRequestParser.DefaultTestTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        // Wrap with plan meta when possible.
        try
        {
            using var doc = JsonDocument.Parse(runJson);
            var root = doc.RootElement.Clone();
            return BuildTestJson.Serialize(new
            {
                schema = TestScene.RunSchemaVersion,
                op = "apply",
                plan = new { filter, include, failed_first = failedFirst },
                result = root
            });
        }
        catch
        {
            return runJson;
        }
    }

    public async Task<string> PublishStructuredAsync(
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken) =>
        await EnqueueToolAsync(
            BuildTestJobKind.PublishStructured,
            args,
            BuildTestToolRequestParser.DefaultPublishTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

    public string GetJobStatus(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!BuildTestToolRequestParser.TryGetString(args, "job_id", out var jobId) || string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("job_id is required.");

        var status = _coordinator.GetJobStatus(jobId);
        if (status is null)
            return BuildTestJson.Serialize(new { found = false, job_id = jobId, message = "Job not found." });

        return BuildTestJson.Serialize(status);
    }

    public string GetJobLog(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!BuildTestToolRequestParser.TryGetString(args, "job_id", out var jobId) || string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("job_id is required.");

        var offset = BuildTestToolRequestParser.TryGetInt(args, "offset_lines", out var parsedOffset)
            ? Math.Max(0, parsedOffset)
            : 0;
        var limit = BuildTestToolRequestParser.TryGetInt(args, "limit_lines", out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, 2000)
            : 200;

        var logChunk = _coordinator.GetJobLogChunk(jobId, offset, limit);
        if (logChunk is null)
            return BuildTestJson.Serialize(new { found = false, job_id = jobId, message = "Job not found." });

        return BuildTestJson.Serialize(logChunk);
    }

    public string CancelJob(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!BuildTestToolRequestParser.TryGetString(args, "job_id", out var jobId) || string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("job_id is required.");

        var result = _coordinator.CancelJob(jobId);
        return BuildTestJson.Serialize(result);
    }

    private async Task<string> EnqueueToolAsync(
        BuildTestJobKind kind,
        IReadOnlyDictionary<string, JsonElement> args,
        int defaultTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var request = BuildTestToolRequestParser.ParseExecutionRequest(args, defaultTimeoutSeconds);
        var sln = SolutionOrProjectPathResolver.Resolve(request.SolutionPath);

        var enqueued = _coordinator.TryEnqueue(
            kind,
            sln,
            request.IncludeRawOutput,
            request.Detail,
            request.TimeoutSeconds,
            request.DotnetOptions);

        if (!enqueued.Accepted)
        {
            return BuildTestJson.Serialize(new BuildTestBusyResponse(
                accepted: false,
                status: "busy",
                retry_after_seconds: enqueued.RetryAfterSeconds,
                message: "Build/test worker is busy. Retry later."));
        }

        if (!request.WaitForCompletion)
        {
            return BuildTestJson.Serialize(new
            {
                accepted = true,
                job_id = enqueued.JobId,
                status = "queued"
            });
        }

        var wait = await _coordinator.WaitForCompletionAsync(enqueued.JobId!, cancellationToken).ConfigureAwait(false);
        if (wait is null)
        {
            return BuildTestJson.Serialize(new
            {
                accepted = true,
                job_id = enqueued.JobId,
                status = "queued",
                message = "Request cancelled while waiting. Use get_job_status."
            });
        }

        return wait;
    }
}
