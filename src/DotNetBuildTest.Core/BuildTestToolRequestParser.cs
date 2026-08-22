using System.Text.Json;

namespace DotNetBuildTest.Core;

/// <summary>Парсинг аргументов MCP-инструментов build/test/publish.</summary>
public static class BuildTestToolRequestParser
{
    public const int DefaultBuildTimeoutSeconds = 600;
    public const int DefaultTestTimeoutSeconds = 900;
    public const int DefaultPublishTimeoutSeconds = 900;

    public static BuildTestExecutionRequest ParseExecutionRequest(
        IReadOnlyDictionary<string, JsonElement> args,
        int defaultTimeoutSeconds)
    {
        if (!TryGetString(args, "solution_path", out var solutionPath) || string.IsNullOrWhiteSpace(solutionPath))
            throw new ArgumentException("solution_path is required.");

        var waitForCompletion = !TryGetBool(args, "wait_for_completion", out var waitValue) || waitValue;
        var includeRawOutput = TryGetBool(args, "include_raw_output", out var includeRaw) && includeRaw
            || TryGetBool(args, "include_raw", out var includeRawAlias) && includeRawAlias;
        TryGetString(args, "detail", out var detailRaw);
        var detail = BuildTestResultDetail.Norm(detailRaw, includeRawOutput);
        var timeoutSeconds = TryGetInt(args, "timeout_seconds", out var timeout)
            ? Math.Clamp(timeout, 5, 3600)
            : defaultTimeoutSeconds;

        var dotnetOptions = DotnetExecutionOptions.Parse(args);
        return new BuildTestExecutionRequest(
            solutionPath,
            waitForCompletion,
            includeRawOutput || detail == BuildTestResultDetail.Full,
            detail,
            timeoutSeconds,
            dotnetOptions);
    }

    public static bool TryGetString(IReadOnlyDictionary<string, JsonElement> args, string key, out string? value)
    {
        if (args.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    public static bool TryGetBool(IReadOnlyDictionary<string, JsonElement> args, string key, out bool value)
    {
        if (args.TryGetValue(key, out var element) &&
            (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
        {
            value = element.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    public static bool TryGetInt(IReadOnlyDictionary<string, JsonElement> args, string key, out int value)
    {
        if (args.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return true;

        value = 0;
        return false;
    }
}
