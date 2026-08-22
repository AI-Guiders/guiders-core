using System.Collections.Concurrent;

namespace DotNetBuildTest.Core;

/// <summary>Last test run summary per solution/project path (agent Test Runner memory).</summary>
public static class TestRunCache
{
    private static readonly ConcurrentDictionary<string, LastRun> ByTarget =
        new(StringComparer.OrdinalIgnoreCase);

    public sealed record FailedItem(string Name, string? Message, int? DurationMs);

    public sealed record LastRun(
        string Target,
        DateTimeOffset AtUtc,
        bool Success,
        int Total,
        int Passed,
        int Failed,
        int Skipped,
        IReadOnlyList<FailedItem> FailedTests,
        string? Filter);

    public static void Remember(
        string target,
        bool success,
        int total,
        int passed,
        int failed,
        int skipped,
        IEnumerable<(string Name, string? Message, int? DurationMs)> failedTests,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;
        var key = Path.GetFullPath(target);
        var items = failedTests
            .Select(t => new FailedItem(t.Name, t.Message, t.DurationMs))
            .Take(100)
            .ToArray();
        ByTarget[key] = new LastRun(
            key,
            DateTimeOffset.UtcNow,
            success,
            total,
            passed,
            failed,
            skipped,
            items,
            filter);
    }

    public static LastRun? TryGet(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;
        return ByTarget.TryGetValue(Path.GetFullPath(target), out var run) ? run : null;
    }

    public static void Clear(string? target = null)
    {
        if (string.IsNullOrWhiteSpace(target))
            ByTarget.Clear();
        else
            ByTarget.TryRemove(Path.GetFullPath(target), out _);
    }
}
