using Cdp.Evidence;

namespace Cdp.Evidence.Tests;

public sealed class EvidenceProjectTests
{
    [Fact]
    public void Build_msbuild_line_becomes_anchor()
    {
        var text = @"D:\src\Foo.cs(12,5): error CS1002: ; expected
    0 Warning(s)
    1 Error(s)";
        var doc = EvidencePreprocess.Project("build", text, new EvidenceContext { ProjectRoot = @"D:\src" });
        Assert.False(doc.Ok);
        Assert.Equal(1, doc.ItemCount);
        var item = doc.Items[0];
        Assert.Equal("CS1002", item.Id);
        Assert.Equal(12, item.Line);
        Assert.Equal(5, item.Column);
        Assert.Contains("[F:", item.Anchor, StringComparison.Ordinal);
        Assert.Contains(";L:12]", item.Anchor, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_stack_in_message_becomes_anchor()
    {
        var msg = "Expected true\nat MyTests.T() in D:\\repo\\MyTests.cs:line 88";
        var doc = EvidencePreprocess.FromFailedTests([("MyTests.T", msg)]);
        Assert.False(doc.Ok);
        Assert.Equal("MyTests.T", doc.Items[0].Id);
        Assert.NotNull(doc.Items[0].Anchor);
        Assert.Contains("MyTests.cs", doc.Items[0].Anchor!, StringComparison.Ordinal);
        Assert.Contains(";L:88]", doc.Items[0].Anchor!, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_detects_build_from_error_CS()
    {
        var doc = EvidencePreprocess.Project("auto", @"C:\a\B.cs(1,1): error CS0246: type missing");
        Assert.Equal("build", doc.Source);
        Assert.Equal(1, doc.ItemCount);
    }

    [Fact]
    public void FromCsxItems_preserves_hints()
    {
        var doc = EvidencePreprocess.FromCsxItems(
        [
            ("CS1061", "error", "SymbolFacade does not contain SearchAsync", 3, 1, "[F:<csx>;L:3]", "try Help")
        ]);
        Assert.Equal("csx", doc.Source);
        Assert.Equal("[F:<csx>;L:3]", doc.Items[0].Anchor);
        Assert.Equal("try Help", doc.Items[0].Hint);
    }

    [Fact]
    public void Slim_default_omits_extra_warnings()
    {
        var text = string.Join('\n',
            Enumerable.Range(1, 10).Select(i => $@"D:\src\Foo.cs({i},1): warning CS0168: unused{i}"));
        var slim = EvidencePreprocess.Project("build", text, new EvidenceContext { ProjectRoot = @"D:\src" });
        Assert.True(slim.Ok);
        Assert.Equal(3, slim.ItemCount);
        Assert.Equal("warnings_omitted_7", slim.Note);

        var fat = EvidencePreprocess.Project(
            "build",
            text,
            new EvidenceContext { ProjectRoot = @"D:\src", IncludeWarnings = true, MaxItems = 80 });
        Assert.Equal(10, fat.ItemCount);
        Assert.Null(fat.Note);
    }

    [Fact]
    public void ToJson_roundtrips_schema()
    {
        var doc = EvidencePreprocess.Project("build", @"X.cs(2): warning CS0168: unused");
        var json = EvidencePreprocess.ToJson(doc);
        Assert.Contains("evidence/v0", json, StringComparison.Ordinal);
        Assert.Contains("\"anchor\"", json, StringComparison.Ordinal);
    }
}
