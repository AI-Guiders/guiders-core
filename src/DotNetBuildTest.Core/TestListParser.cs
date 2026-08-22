namespace DotNetBuildTest.Core;

/// <summary>Parse <c>dotnet test --list-tests</c> output into FQNs.</summary>
public static class TestListParser
{
    public const int MaxDefault = 500;

    public static IReadOnlyList<string> Parse(string output, int max = MaxDefault)
    {
        max = Math.Clamp(max, 1, 5000);
        if (string.IsNullOrEmpty(output))
            return [];

        var list = new List<string>();
        var started = false;
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var line = raw.TrimEnd();
            if (!started)
            {
                if (line.Contains("The following Tests are available", StringComparison.OrdinalIgnoreCase))
                    started = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Listed tests are indented; stop on blank section noise / next banner.
            var name = line.Trim();
            if (name.StartsWith("Test run for", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("A total of", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Building", StringComparison.OrdinalIgnoreCase))
                break;

            if (name.Contains(' ') && !name.Contains('.'))
                continue; // unlikely FQN

            list.Add(name);
            if (list.Count >= max)
                break;
        }

        return list;
    }
}
