using DotNetWorkspace.Core;

namespace DotNetWorkspace.Core.Tests;

public sealed class SolutionLanguageRulesTests
{
    [Fact]
    public void InferComposition_mixed_slnx()
    {
        var root = CreateMixedWorkspace();
        try
        {
            var graph = DotNetWorkspace.Load(Path.Combine(root, "Mixed.slnx"));
            Assert.Equal(SolutionLanguageComposition.Mixed, SolutionLanguageRules.InferComposition(graph));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryInferFromAnchor_fsharp_only_slnx()
    {
        var root = CreateFSharpOnlyWorkspace();
        try
        {
            Assert.Equal(
                SolutionLanguageComposition.FSharpOnly,
                SolutionLanguageRules.TryInferFromAnchor(Path.Combine(root, "Model.slnx")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreateMixedWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "dnw-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));

        File.WriteAllText(
            Path.Combine(root, "app", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(root, "lib", "Lib.fsproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="Lib.fs" /></ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(root, "lib", "Lib.fs"), "module Lib\nlet x = 1");

        File.WriteAllText(
            Path.Combine(root, "Mixed.slnx"),
            """
            <Solution>
              <Folder Name="/app/">
                <Project Path="app/App.csproj" />
              </Folder>
              <Folder Name="/lib/">
                <Project Path="lib/Lib.fsproj" />
              </Folder>
            </Solution>
            """);

        return root;
    }

    static string CreateFSharpOnlyWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "dnw-fsonly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));

        File.WriteAllText(
            Path.Combine(root, "src", "Model.fsproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="Model.fs" /></ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(root, "src", "Model.fs"), "module Model\nlet x = 1");

        File.WriteAllText(
            Path.Combine(root, "Model.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Model.fsproj" />
              </Folder>
            </Solution>
            """);

        return root;
    }
}
