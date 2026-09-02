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
