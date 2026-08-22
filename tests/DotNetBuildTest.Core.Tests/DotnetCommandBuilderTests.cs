using DotNetBuildTest.Core;

namespace DotNetBuildTest.Core.Tests;

public sealed class DotnetCommandBuilderTests
{
    [Fact]
    public void BuildBuildArgs_includes_configuration_framework_no_restore_and_extra()
    {
        var o = new DotnetExecutionOptions("Release", "net10.0", true, false, null, null, ["-v", "m"]);
        var args = DotnetCommandBuilder.BuildBuildArgs(@"C:\a\b.sln", o);

        Assert.Equal(
            new[] { "build", @"C:\a\b.sln", "-c", "Release", "-f", "net10.0", "--no-restore", "-v", "m" },
            args);
    }

    [Fact]
    public void BuildTestArgs_inserts_no_build_and_filter_before_shared_flags()
    {
        var o = new DotnetExecutionOptions("Debug", null, false, true, "FullyQualifiedName~X", null, []);
        var args = DotnetCommandBuilder.BuildTestArgs("proj.csproj", o);

        Assert.Contains("--no-build", args);
        Assert.Contains("--filter", args);
        var filterIdx = args.IndexOf("--filter");
        Assert.Equal("FullyQualifiedName~X", args[filterIdx + 1]);
        var loggerIdx = args.IndexOf("--logger");
        Assert.Equal("console;verbosity=detailed", args[loggerIdx + 1]);
    }

    [Fact]
    public void BuildPublishArgs_outputs_o_and_no_build()
    {
        var o = new DotnetExecutionOptions("Release", "net10.0", true, true, null, @"D:\out", []);
        var args = DotnetCommandBuilder.BuildPublishArgs("app.csproj", o);

        Assert.Equal("publish", args[0]);
        Assert.Equal("app.csproj", args[1]);
        Assert.Equal("-o", args[2]);
        Assert.Equal(@"D:\out", args[3]);
        Assert.Contains("--no-build", args);
        Assert.Contains("--no-restore", args);
    }
}
