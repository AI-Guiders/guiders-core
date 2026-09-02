using DotNetWorkspace.Core;

namespace DotNetWorkspace.Core.Tests;

public sealed class SolutionProjectGraphTests
{
    [Fact]
    public void Resolves_fsproj_from_slnx_graph()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var fs = Path.Combine(root, "lib", "Lib.fs");
            var entry = DotNetWorkspace.TryResolveOwningProject(
                fs,
                Path.Combine(root, "Mixed.slnx"),
                DotNetProjectKind.FSharp);

            Assert.NotNull(entry);
            Assert.Equal(DotNetProjectKind.FSharp, entry!.Kind);
            Assert.EndsWith("Lib.fsproj", entry.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolves_csproj_from_slnx_graph()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var cs = Path.Combine(root, "app", "App.cs");
            var entry = DotNetWorkspace.TryResolveOwningProject(
                cs,
                Path.Combine(root, "Mixed.slnx"),
                DotNetProjectKind.CSharp);

            Assert.NotNull(entry);
            Assert.Equal(DotNetProjectKind.CSharp, entry!.Kind);
            Assert.EndsWith("App.csproj", entry.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Walk_up_without_solution_hint()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var fs = Path.Combine(root, "lib", "Lib.fs");
            var entry = DotNetWorkspace.TryResolveOwningProject(fs, kindFilter: DotNetProjectKind.FSharp);

            Assert.NotNull(entry);
            Assert.EndsWith("Lib.fsproj", entry!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "dnw-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));

        File.WriteAllText(
            Path.Combine(root, "app", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(root, "app", "App.cs"), "namespace App; public class X {}");

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
}
