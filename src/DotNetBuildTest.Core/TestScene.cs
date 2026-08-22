using System.Text.Json;

namespace DotNetBuildTest.Core;

/// <summary>Agent Test Runner map — discover FQNs + last run (schema <c>test_scene/v0</c>).</summary>
public static class TestScene
{
    public const string SchemaVersion = "test_scene/v0";
    public const string RunSchemaVersion = "test_run/v0";

    public static async Task<string> SceneAsync(
        string solutionOrProjectPath,
        DotnetExecutionOptions options,
        int maxTests,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var target = SolutionOrProjectPathResolver.Resolve(solutionOrProjectPath);
        maxTests = Math.Clamp(maxTests, 1, 5000);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 10, 600);

        var workingDir = Path.GetDirectoryName(target) ?? "";
        var run = await DotnetProcessRunner.RunAsync(
            workingDir,
            DotnetCommandBuilder.BuildListTestsArgs(target, options),
            timeoutSeconds,
            ct,
            onLogLine: null,
            options.SupplementalEnvironmentVariables).ConfigureAwait(false);

        var tests = TestListParser.Parse(run.Output, maxTests);
        var last = TestRunCache.TryGet(target);

        var payload = new
        {
            schema = SchemaVersion,
            ok = run.ExitCode == 0 && !run.TimedOut && !run.Cancelled,
            target,
            discover = new
            {
                count = tests.Count,
                truncated = tests.Count >= maxTests,
                tests,
                exit_code = run.ExitCode,
                timed_out = run.TimedOut,
                cancelled = run.Cancelled,
                failure_reason = run.FailureReason
            },
            last_run = last is null ? null : new
            {
                at_utc = last.AtUtc,
                success = last.Success,
                total = last.Total,
                passed = last.Passed,
                failed = last.Failed,
                skipped = last.Skipped,
                filter = last.Filter,
                failed_tests = last.FailedTests.Select(f => new
                {
                    name = f.Name,
                    message = f.Message,
                    duration_ms = f.DurationMs
                }).ToArray()
            },
            next = new
            {
                plan = "cdp_test_plan",
                hint = "Pick discover.tests[] or failed_first=true → op=apply. Prefer scene before shell archaeology."
            }
        };

        return BuildTestJson.Serialize(payload);
    }

    public static string PlanPreview(
        string solutionOrProjectPath,
        IReadOnlyList<string> include,
        bool failedFirst,
        string? rawFilter)
    {
        var target = SolutionOrProjectPathResolver.Resolve(solutionOrProjectPath);
        var last = TestRunCache.TryGet(target);
        string? filter = null;
        string source;

        if (!string.IsNullOrWhiteSpace(rawFilter))
        {
            filter = rawFilter.Trim();
            source = "filter";
        }
        else if (failedFirst)
        {
            filter = TestPlanFilter.FromFailedFirst(last);
            source = "failed_first";
        }
        else if (include.Count > 0)
        {
            filter = TestPlanFilter.FromIncludes(include);
            source = "include";
        }
        else
        {
            source = "all";
        }

        return BuildTestJson.Serialize(new
        {
            schema = "test_plan/v0",
            op = "preview",
            target,
            source,
            include,
            failed_first = failedFirst,
            filter,
            last_failed_count = last?.FailedTests.Count ?? 0,
            next = new { apply = "cdp_test_plan op=apply with same include|failed_first|filter" }
        });
    }

    public static IReadOnlyList<string> ParseInclude(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("include", out var el))
            return [];

        if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } one)
            return [one];

        if (el.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                list.Add(s);
        }

        return list;
    }
}
