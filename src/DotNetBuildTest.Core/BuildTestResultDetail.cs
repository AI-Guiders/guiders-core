using DotNetBuildTestParsers;

namespace DotNetBuildTest.Core;

/// <summary>
/// Agent-facing result depth for build/test/publish.
/// Default <c>auto</c>: green → pulse counts only; fail → failed/errors (+ capped evidence).
/// </summary>
public static class BuildTestResultDetail
{
    public const string Auto = "auto";
    public const string Pulse = "pulse";
    public const string Slim = "slim";
    public const string Full = "full";

    public static string Norm(string? raw, bool includeRawOutput)
    {
        if (includeRawOutput)
            return Full;

        var d = (raw ?? Auto).Trim().ToLowerInvariant();
        return d switch
        {
            "pulse" or "ok" or "summary" => Pulse,
            "slim" or "fail" or "failed" => Slim,
            "full" or "raw" => Full,
            _ => Auto
        };
    }

    /// <summary>Resolve auto relative to success: green→pulse, fail→slim.</summary>
    public static string Effective(string detail, bool success) =>
        detail switch
        {
            Auto => success ? Pulse : Slim,
            _ => detail
        };

    public static object ShapeTest(
        string detailRequested,
        bool success,
        string pulse,
        TestParseResult parsed,
        object? evidenceDto,
        string? filter,
        string jobId,
        string status,
        bool timedOut,
        bool cancelled,
        string? failureReason,
        int durationMs,
        string? rawOutput)
    {
        var eff = Effective(detailRequested, success);
        if (eff == Pulse)
        {
            return new
            {
                schema = TestScene.RunSchemaVersion,
                success,
                pulse,
                detail = Pulse,
                total = parsed.Total,
                passed = parsed.Passed,
                failed = parsed.Failed,
                skipped = parsed.Skipped,
                filter,
                job_id = jobId,
                status,
                timed_out = timedOut,
                cancelled,
                failure_reason = failureReason,
                duration_ms = durationMs,
                hint = success
                    ? "green — pulse only; detail=slim|full or include_raw_output=true for more"
                    : "detail=slim for failed_tests; include_raw_output=true for full pipe"
            };
        }

        return new
        {
            schema = TestScene.RunSchemaVersion,
            success,
            pulse,
            detail = eff,
            total = parsed.Total,
            passed = parsed.Passed,
            failed = parsed.Failed,
            skipped = parsed.Skipped,
            failed_tests = parsed.FailedTests.Select(t => new { t.Name, t.Message, duration_ms = t.DurationMs }).ToArray(),
            evidence = evidenceDto,
            filter,
            job_id = jobId,
            status,
            timed_out = timedOut,
            cancelled,
            failure_reason = failureReason,
            duration_ms = durationMs,
            raw_output = eff == Full ? rawOutput : null,
            hint = eff == Full
                ? null
                : "include_raw_output=true / detail=full for full pipe + all warnings in evidence"
        };
    }

    public static object ShapeBuild(
        string detailRequested,
        bool success,
        string pulse,
        BuildParseResult parsed,
        object? evidenceDto,
        string jobId,
        string status,
        bool timedOut,
        bool cancelled,
        string? failureReason,
        int durationMs,
        string? rawOutput,
        bool includeWarningsFull)
    {
        var eff = Effective(detailRequested, success);
        if (eff == Pulse)
        {
            return new
            {
                success,
                pulse,
                detail = Pulse,
                exit_code = parsed.ExitCode,
                error_count = parsed.Errors.Count,
                warning_count = parsed.Warnings.Count,
                job_id = jobId,
                status,
                timed_out = timedOut,
                cancelled,
                failure_reason = failureReason,
                duration_ms = durationMs,
                hint = success
                    ? "green — pulse only; detail=slim|full for errors/warnings"
                    : "detail=slim for errors[]; include_raw_output=true for full pipe"
            };
        }

        return new
        {
            success,
            pulse,
            detail = eff,
            exit_code = parsed.ExitCode,
            error_count = parsed.Errors.Count,
            warning_count = parsed.Warnings.Count,
            errors = parsed.Errors.Select(e => new { e.File, e.Line, e.Column, e.Code, e.Message }).ToArray(),
            warnings = includeWarningsFull || eff == Full
                ? parsed.Warnings.Select(w => new { w.File, w.Line, w.Column, w.Code, w.Message }).ToArray()
                : parsed.Warnings.Take(3).Select(w => new { w.File, w.Line, w.Column, w.Code, w.Message }).ToArray(),
            evidence = evidenceDto,
            job_id = jobId,
            status,
            timed_out = timedOut,
            cancelled,
            failure_reason = failureReason,
            duration_ms = durationMs,
            raw_output = eff == Full ? rawOutput : null,
            hint = eff == Full
                ? null
                : "include_raw_output=true / detail=full for full warnings[] + raw_output"
        };
    }
}
