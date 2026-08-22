using Cdp.Core;
using Xunit;

namespace Cdp.Core.Tests;

public class ProjectOpenDetectorTests
{
    private static readonly LanguageRegistry Langs = LanguageRegistry.Default;

    [Fact]
    public void Detect_Sln_File()
    {
        var dir = CreateTempDir();
        try
        {
            var sln = Path.Combine(dir, "App.sln");
            File.WriteAllText(sln, "Microsoft Visual Studio Solution File");
            var r = Langs.Detect(sln);
            Assert.Equal("sln", r.Kind);
            Assert.Equal(CdpLanguages.Csharp, r.Language);
            Assert.Equal(dir, r.Root);
            Assert.Equal(sln, r.SolutionOrProjectPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Detect_Tsconfig_In_Directory()
    {
        var dir = CreateTempDir();
        try
        {
            var ts = Path.Combine(dir, "tsconfig.json");
            File.WriteAllText(ts, """{ "compilerOptions": { "strict": true } }""");
            var nested = Path.Combine(dir, "src");
            Directory.CreateDirectory(nested);
            var r = Langs.Detect(nested);
            Assert.Equal("tsconfig", r.Kind);
            Assert.Equal(CdpLanguages.Typescript, r.Language);
            Assert.Equal(dir, r.Root);
            Assert.Equal(ts, r.TsConfigPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Detect_Prefers_Sln_Over_Tsconfig()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "App.sln"), "sln");
            File.WriteAllText(Path.Combine(dir, "tsconfig.json"), "{}");
            var r = Langs.Detect(dir);
            Assert.Equal("sln", r.Kind);
            Assert.Equal(CdpLanguages.Csharp, r.Language);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryNormalize_Typescript_Aliases()
    {
        Assert.True(Langs.TryNormalize("ts", out var lang));
        Assert.Equal(CdpLanguages.Typescript, lang);
        Assert.True(Langs.TryNormalize("typescript", out lang));
        Assert.Equal(CdpLanguages.Typescript, lang);
    }

    [Fact]
    public void Detect_Ps1_File()
    {
        var dir = CreateTempDir();
        try
        {
            var ps1 = Path.Combine(dir, "deploy.ps1");
            File.WriteAllText(ps1, "Write-Output 'ok'\n");
            var r = Langs.Detect(ps1);
            Assert.Equal("ps1", r.Kind);
            Assert.Equal(CdpLanguages.PowerShell, r.Language);
            Assert.Equal(dir, r.Root);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryNormalize_Powershell_Aliases()
    {
        Assert.True(Langs.TryNormalize("ps1", out var lang));
        Assert.Equal(CdpLanguages.PowerShell, lang);
        Assert.True(Langs.TryNormalize("pwsh", out lang));
        Assert.Equal(CdpLanguages.PowerShell, lang);
    }

    [Fact]
    public void Config_Can_Add_Language_Without_Enum()
    {
        var reg = new LanguageRegistry(
            ids: [CdpLanguages.Csharp, "rust"],
            aliases: [new("rs", "rust")],
            detectRules:
            [
                new("rust", "cargo", 5, FileName: "Cargo.toml"),
                new(CdpLanguages.Csharp, "csproj", 20, Extension: ".csproj"),
            ]);
        Assert.True(reg.TryNormalize("rs", out var id));
        Assert.Equal("rust", id);

        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Cargo.toml"), "[package]");
            var r = reg.Detect(dir);
            Assert.Equal("rust", r.Language);
            Assert.Equal("cargo", r.Kind);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
