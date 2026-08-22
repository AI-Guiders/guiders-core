namespace DotNetBuildTest.Core;

/// <summary>Build VSTest <c>--filter</c> from FQNs / failed-first / raw filter.</summary>
public static class TestPlanFilter
{
    public static string? FromIncludes(IReadOnlyList<string> includes)
    {
        if (includes.Count == 0)
            return null;

        // Exact FQN match OR'd — works for FullyQualifiedName=… expressions.
        var parts = new List<string>(includes.Count);
        foreach (var raw in includes)
        {
            var s = raw.Trim();
            if (s.Length == 0)
                continue;
            if (s.Contains('=', StringComparison.Ordinal) || s.Contains('~', StringComparison.Ordinal)
                || s.Contains('|', StringComparison.Ordinal))
            {
                parts.Add(s); // already a filter fragment
                continue;
            }

            parts.Add("FullyQualifiedName=" + s);
        }

        return parts.Count == 0 ? null : string.Join("|", parts);
    }

    public static string? FromFailedFirst(TestRunCache.LastRun? last)
    {
        if (last is null || last.FailedTests.Count == 0)
            return null;
        return FromIncludes(last.FailedTests.Select(f => f.Name).ToArray());
    }
}
