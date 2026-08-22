using DotNetBuildTest.Core;

namespace DotNetBuildTest.Core.Tests;

public sealed class SolutionOrProjectPathResolverTests
{
    [Fact]
    public void Resolve_returns_existing_csproj_path()
    {
        var dir = Directory.CreateTempSubdirectory("dotnet-core-csproj-");
        try
        {
            var csproj = Path.Combine(dir.FullName, "App.csproj");
            File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var resolved = SolutionOrProjectPathResolver.Resolve(csproj);

            Assert.Equal(Path.GetFullPath(csproj), resolved);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Resolve_directory_prefers_sln_over_csproj()
    {
        var dir = Directory.CreateTempSubdirectory("dotnet-core-sln-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "a.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            var sln = Path.Combine(dir.FullName, "x.sln");
            File.WriteAllText(sln, "");

            var resolved = SolutionOrProjectPathResolver.Resolve(dir.FullName);

            Assert.Equal(Path.GetFullPath(sln), resolved);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Resolve_directory_single_csproj_without_sln()
    {
        var dir = Directory.CreateTempSubdirectory("dotnet-core-oneproj-");
        try
        {
            var csproj = Path.Combine(dir.FullName, "Only.csproj");
            File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var resolved = SolutionOrProjectPathResolver.Resolve(dir.FullName);

            Assert.Equal(Path.GetFullPath(csproj), resolved);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Resolve_directory_multiple_csproj_without_sln_throws()
    {
        var dir = Directory.CreateTempSubdirectory("dotnet-core-multiproj-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "A.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(dir.FullName, "B.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

            var ex = Assert.Throws<ArgumentException>(() => SolutionOrProjectPathResolver.Resolve(dir.FullName));
            Assert.Contains("Multiple .csproj", ex.Message);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Resolve_returns_existing_slnx_path()
    {
        var dir = Directory.CreateTempSubdirectory("dotnet-core-slnx-");
        try
        {
            var slnx = Path.Combine(dir.FullName, "App.slnx");
            File.WriteAllText(slnx, "<Solution></Solution>");

            var resolved = SolutionOrProjectPathResolver.Resolve(slnx);

            Assert.Equal(Path.GetFullPath(slnx), resolved);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Resolve_non_project_file_throws()
    {
        var dir = Directory.CreateTempSubdirectory("dotnet-core-txt-");
        try
        {
            var txt = Path.Combine(dir.FullName, "readme.txt");
            File.WriteAllText(txt, "x");

            var ex = Assert.Throws<ArgumentException>(() => SolutionOrProjectPathResolver.Resolve(txt));
            Assert.Contains("solution/project", ex.Message);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch { /* ignore */ }
        }
    }
}
