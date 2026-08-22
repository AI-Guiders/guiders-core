namespace DotNetBuildTest.Core;

/// <summary>Сборка списка аргументов для <see cref="DotnetProcessRunner"/> (без shell, только ArgumentList).</summary>
public static class DotnetCommandBuilder
{
    public static List<string> BuildBuildArgs(string solutionPath, DotnetExecutionOptions o)
    {
        var a = new List<string> { "build", solutionPath };
        AppendShared(a, o, includeFilter: false);
        return a;
    }

    public static List<string> BuildTestArgs(string solutionPath, DotnetExecutionOptions o)
    {
        var a = new List<string> { "test", solutionPath, "--logger", "console;verbosity=detailed" };
        if (o.NoBuild)
            a.Add("--no-build");
        AppendShared(a, o, includeFilter: true);
        return a;
    }

    public static List<string> BuildListTestsArgs(string solutionPath, DotnetExecutionOptions o)
    {
        var a = new List<string> { "test", solutionPath, "--list-tests" };
        AppendShared(a, o, includeFilter: true);
        return a;
    }

    public static List<string> BuildPublishArgs(string solutionPath, DotnetExecutionOptions o)
    {
        var a = new List<string> { "publish", solutionPath };
        if (!string.IsNullOrWhiteSpace(o.PublishOutputPath))
        {
            a.Add("-o");
            a.Add(o.PublishOutputPath);
        }

        if (o.NoBuild)
            a.Add("--no-build");

        AppendShared(a, o, includeFilter: false);
        return a;
    }

    private static void AppendShared(List<string> a, DotnetExecutionOptions o, bool includeFilter)
    {
        if (!string.IsNullOrWhiteSpace(o.Configuration))
        {
            a.Add("-c");
            a.Add(o.Configuration);
        }

        if (!string.IsNullOrWhiteSpace(o.Framework))
        {
            a.Add("-f");
            a.Add(o.Framework);
        }

        if (o.NoRestore)
            a.Add("--no-restore");

        if (includeFilter && !string.IsNullOrWhiteSpace(o.Filter))
        {
            a.Add("--filter");
            a.Add(o.Filter!);
        }

        foreach (var extra in o.AdditionalArguments)
            a.Add(extra);
    }
}
