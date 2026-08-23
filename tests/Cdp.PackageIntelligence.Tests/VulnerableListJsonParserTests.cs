using Cdp.PackageIntelligence.Internal;

namespace Cdp.PackageIntelligence.Tests;

public sealed class VulnerableListJsonParserTests
{
    const string Sample = """
        {
          "version": 1,
          "sources": ["https://api.nuget.org/v3/index.json"],
          "projects": [{
            "path": "C:/proj/App.csproj",
            "frameworks": [{
              "framework": "net10.0",
              "topLevelPackages": [{
                "id": "Newtonsoft.Json",
                "requestedVersion": "11.0.2",
                "resolvedVersion": "11.0.2",
                "vulnerabilities": [{
                  "severity": "High",
                  "advisoryurl": "https://github.com/advisories/GHSA-5crp-9r3c-p9vr"
                }]
              }]
            }]
          }]
        }
        """;

    [Fact]
    public void Parse_finds_vulnerable_top_level_package()
    {
        var result = VulnerableListJsonParser.Parse("C:/proj/App.csproj", Sample);
        Assert.True(result.HasVulnerabilities);
        Assert.Single(result.Packages);
        var pkg = result.Packages[0];
        Assert.Equal("Newtonsoft.Json", pkg.PackageId);
        Assert.Equal("11.0.2", pkg.ResolvedVersion);
        Assert.False(pkg.IsTransitive);
        Assert.Equal("High", pkg.Advisories[0].Severity);
    }

    [Fact]
    public void Parse_empty_projects_is_clean()
    {
        var result = VulnerableListJsonParser.Parse("C:/proj/App.csproj", """{"projects":[{"path":"C:/proj/App.csproj"}]}""");
        Assert.False(result.HasVulnerabilities);
        Assert.Empty(result.Packages);
    }
}
