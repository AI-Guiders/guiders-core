using DotNetBuildTest.Core;

namespace DotNetBuildTest.Core.Tests;

public sealed class TestListParserTests
{
    [Fact]
    public void Parse_extracts_fqns_after_banner()
    {
        var output = """
            Test run for C:\\a\\t.dll
            The following Tests are available:
                Ns.A.One
                Ns.A.Two
            """;
        var tests = TestListParser.Parse(output);
        Assert.Equal(["Ns.A.One", "Ns.A.Two"], tests);
    }

    [Fact]
    public void PlanFilter_builds_or_of_exact_fqns()
    {
        var f = TestPlanFilter.FromIncludes(["A.B.C", "D.E.F"]);
        Assert.Equal("FullyQualifiedName=A.B.C|FullyQualifiedName=D.E.F", f);
    }

    [Fact]
    public void BuildListTestsArgs_includes_list_tests()
    {
        var args = DotnetCommandBuilder.BuildListTestsArgs("p.csproj", DotnetExecutionOptions.Empty);
        Assert.Equal(["test", "p.csproj", "--list-tests"], args);
    }
}
