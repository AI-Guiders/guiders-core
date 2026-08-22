using Xunit;

namespace DotNetBuildTestParsers.Tests;

public sealed class ParsersTests
{
    [Fact]
    public void BuildOutputParser_finds_error_and_warning()
    {
        const string output = """
            D:\app\Program.cs(10,5): error CS1002: ; expected
            D:\app\Program.cs(12,1): warning CS0162: Unreachable code
            """;

        var r = BuildOutputParser.Parse(output);

        Assert.Single(r.Errors);
        Assert.Single(r.Warnings);
        Assert.Equal("D:\\app\\Program.cs", r.Errors[0].File);
        Assert.Equal(10, r.Errors[0].Line);
        Assert.Equal(5, r.Errors[0].Column);
        Assert.Equal("CS1002", r.Errors[0].Code);
    }

    [Fact]
    public void TestOutputParser_counts_passed_failed()
    {
        const string output = """
            Passed TestProject.UnitTest1 [1 ms]
            Failed TestProject.UnitTest2 [2 ms]
              Error Message: Assert.Equal() Failure
            """;

        var r = TestOutputParser.Parse(output);

        Assert.Equal(2, r.Total);
        Assert.Equal(1, r.Passed);
        Assert.Equal(1, r.Failed);
        Assert.Single(r.FailedTests);
        Assert.Equal("TestProject.UnitTest2", r.FailedTests[0].Name);
        Assert.Contains("Assert.Equal", r.FailedTests[0].Message ?? "");
    }

    [Fact]
    public void TestOutputParser_empty_is_not_success()
    {
        var r = TestOutputParser.Parse("");
        Assert.Equal(0, r.Total);
        Assert.True(r.Empty);
        Assert.False(r.Success);
    }
}
