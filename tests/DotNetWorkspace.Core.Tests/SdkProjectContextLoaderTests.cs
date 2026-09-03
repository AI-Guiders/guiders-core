using DotNetWorkspace.Core;

namespace DotNetWorkspace.Core.Tests;

public sealed class SdkProjectContextLoaderTests
{
    [Fact]
    public void Load_with_ensure_build_reaches_built_phase_for_fsproj()
    {
        var root = CreateProjectRoot();
        try
        {
            var fsproj = Path.Combine(root, "SemProj.fsproj");
            var options = new ProjectContextLoadOptions(EnsureRestore: true, EnsureBuild: true);
            var ctx = new PhasedSdkProjectContextLoader().Load(fsproj, options);

            Assert.True((int)ctx.Phase >= (int)ProjectContextPhase.Built);
            Assert.Equal(ProjectContextPhase.Compile, ctx.Phase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_reaches_compile_phase_for_sdk_fsproj()
    {
        var root = CreateProjectRoot();
        try
        {
            var fsproj = Path.Combine(root, "SemProj.fsproj");
            var ctx = new PhasedSdkProjectContextLoader().Load(fsproj);

            Assert.Equal(ProjectContextPhase.Compile, ctx.Phase);
            Assert.NotEmpty(ctx.ReferenceAssemblies);
            Assert.Contains(ctx.SourceFiles, f => f.EndsWith("Sem.fs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_includes_project_reference_assemblies_for_fsproj()
    {
        var root = Path.Combine(Path.GetTempPath(), "sdk-projref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var libDir = Path.Combine(root, "Lib");
        Directory.CreateDirectory(libDir);

        var libProj = Path.Combine(libDir, "Lib.fsproj");
        var libFs = Path.Combine(libDir, "Lib.fs");
        var appProj = Path.Combine(root, "App.fsproj");
        var appFs = Path.Combine(root, "App.fs");

        File.WriteAllText(
            libProj,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="Lib.fs" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(libFs, "module Lib\nlet value = 1\n");

        File.WriteAllText(
            appProj,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="Lib\Lib.fsproj" />
                <Compile Include="App.fs" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(appFs, "module App\nopen Lib\nlet x = value\n");

        try
        {
            var options = new ProjectContextLoadOptions(EnsureRestore: true, EnsureBuild: true);
            var ctx = new PhasedSdkProjectContextLoader().Load(appProj, options);

            Assert.Equal(ProjectContextPhase.Compile, ctx.Phase);
            Assert.Contains(
                ctx.ReferenceAssemblies,
                r => r.EndsWith("Lib.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_includes_framework_refs_for_guiders_fsharp_adapters_fsproj()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "guiders-fsharp"));
        var fsproj = Path.Combine(
            repoRoot,
            "src",
            "AIGuiders.Platform.Modeling.Language.Adapters.Fcs",
            "AIGuiders.Platform.Modeling.Language.Adapters.Fcs.fsproj");

        if (!File.Exists(fsproj))
            return;

        var ctx = new PhasedSdkProjectContextLoader().Load(fsproj, WorkspaceProjectWarm.FSharpWarmOptions);

        Assert.Contains(
            ctx.ReferenceAssemblies,
            r => r.Contains("System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
    }

    static string CreateProjectRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "sdk-ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "SemProj.fsproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="Sem.fs" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Sem.fs"), "module Sem\nlet x = 1\n");
        return root;
    }
}
