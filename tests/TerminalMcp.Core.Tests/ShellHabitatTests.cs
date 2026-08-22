using System.Text.Json;
using TerminalMcp.Core;

namespace TerminalMcp.Core.Tests;

public sealed class ShellHabitatTests
{
    [Fact]
    public void Finished_event_fires_on_foreground_run()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var echo = OperatingSystem.IsWindows() ? "Write-Output fin-evt" : "echo fin-evt";
        ShellFinishedInfo? got = null;
        h.Finished += info => got = info;

        _ = h.Run(defaults, echo, "fin", null, null, 30);

        Assert.NotNull(got);
        Assert.Equal("fin", got!.Tab);
        Assert.Equal(0, got.ExitCode);
        Assert.False(got.Background);
        Assert.Contains("fin-evt", got.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_ensures_main_tab()
    {
        var h = new ShellHabitat();
        using var doc = JsonDocument.Parse(h.Scene());
        var root = doc.RootElement;
        Assert.Equal("shell_scene/v0", root.GetProperty("schema").GetString());
        Assert.Equal(1, root.GetProperty("tab_count").GetInt32());
        Assert.Equal("main", root.GetProperty("tabs")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void Run_history_rerun_last_roundtrip()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var echo = OperatingSystem.IsWindows()
            ? "Write-Output 'habitat-ok'"
            : "echo habitat-ok";

        using (var runDoc = JsonDocument.Parse(h.Run(defaults, echo, "build", null, null, 30)))
        {
            var run = runDoc.RootElement;
            Assert.True(run.GetProperty("ok").GetBoolean());
            Assert.Equal("build", run.GetProperty("tab").GetString());
            Assert.Contains("habitat-ok", run.GetProperty("stdout").GetString()!, StringComparison.Ordinal);
            Assert.Equal(0, run.GetProperty("history_index").GetInt32());
        }

        using (var histDoc = JsonDocument.Parse(h.History("build", 10)))
        {
            Assert.Equal(1, histDoc.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(echo, histDoc.RootElement.GetProperty("items")[0].GetProperty("Command").GetString());
        }

        using (var rerunDoc = JsonDocument.Parse(h.Rerun(defaults, "build", null, 30)))
        {
            Assert.True(rerunDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(1, rerunDoc.RootElement.GetProperty("history_index").GetInt32());
        }

        using var lastDoc = JsonDocument.Parse(h.Last("build", 2000));
        Assert.False(lastDoc.RootElement.GetProperty("empty").GetBoolean());
        Assert.Contains("habitat-ok", lastDoc.RootElement.GetProperty("stdout").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_shows_two_tabs()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var echo = OperatingSystem.IsWindows() ? "Write-Output a" : "echo a";
        _ = h.Run(defaults, echo, "t1", null, null, 30);
        _ = h.Run(defaults, echo, "t2", null, null, 30);
        using var doc = JsonDocument.Parse(h.Scene());
        Assert.Equal(2, doc.RootElement.GetProperty("tab_count").GetInt32());
    }

    [Fact]
    public void ManyTabs_beyond_former_limit_ok()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var echo = OperatingSystem.IsWindows() ? "Write-Output x" : "echo x";
        for (var i = 0; i < 12; i++)
            _ = h.Run(defaults, echo, $"t{i}", null, null, 30);

        using var doc = JsonDocument.Parse(h.Scene());
        Assert.Equal(12, doc.RootElement.GetProperty("tab_count").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("max_tabs", out _));
    }

    [Fact]
    public void Which_reports_shell()
    {
        var h = new ShellHabitat();
        using var doc = JsonDocument.Parse(h.Which("main"));
        Assert.Equal("shell_which/v0", doc.RootElement.GetProperty("schema").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("shell_exe").GetString()));
    }

    [Fact]
    public void Background_run_kill_and_close()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var sleep = OperatingSystem.IsWindows()
            ? "Start-Sleep -Seconds 30"
            : "sleep 30";

        using (var runDoc = JsonDocument.Parse(h.Run(defaults, sleep, "serve", null, null, null, background: true)))
        {
            var run = runDoc.RootElement;
            Assert.True(run.GetProperty("ok").GetBoolean());
            Assert.True(run.GetProperty("background").GetBoolean());
            Assert.Equal("running", run.GetProperty("state").GetString());
            Assert.True(run.GetProperty("pid").GetInt32() > 0);
        }

        using (var sceneDoc = JsonDocument.Parse(h.Scene()))
        {
            var tab = sceneDoc.RootElement.GetProperty("tabs").EnumerateArray()
                .First(t => t.GetProperty("id").GetString() == "serve");
            Assert.Equal("running", tab.GetProperty("state").GetString());
        }

        using (var killDoc = JsonDocument.Parse(h.Kill("serve")))
        {
            Assert.True(killDoc.RootElement.GetProperty("killed").GetBoolean());
        }

        Thread.Sleep(300);
        using (var lastDoc = JsonDocument.Parse(h.Last("serve", 1000)))
        {
            Assert.NotEqual("running", lastDoc.RootElement.GetProperty("state").GetString());
        }

        using var closeDoc = JsonDocument.Parse(h.Close("serve"));
        Assert.True(closeDoc.RootElement.GetProperty("closed").GetBoolean());
    }

    [Fact]
    public void Close_removes_tab_from_scene()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var echo = OperatingSystem.IsWindows() ? "Write-Output x" : "echo x";
        _ = h.Run(defaults, echo, "t0", null, null, 30);
        _ = h.Run(defaults, echo, "t1", null, null, 30);

        using (var closeDoc = JsonDocument.Parse(h.Close("t0")))
            Assert.True(closeDoc.RootElement.GetProperty("closed").GetBoolean());

        using var scene = JsonDocument.Parse(h.Scene());
        var ids = scene.RootElement.GetProperty("tabs").EnumerateArray()
            .Select(t => t.GetProperty("id").GetString())
            .ToArray();
        Assert.DoesNotContain("t0", ids);
        Assert.Contains("t1", ids);
    }

    [Fact]
    public void SetLocation_persists_on_tab()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ".."));
        var cd = OperatingSystem.IsWindows()
            ? $"Set-Location '{parent.Replace("'", "''", StringComparison.Ordinal)}'"
            : $"cd '{parent}'";
        _ = h.Run(defaults, cd, "nav", null, null, 30);
        using var doc = JsonDocument.Parse(h.Run(defaults, OperatingSystem.IsWindows()
            ? "(Get-Location).Path"
            : "pwd", "nav", null, null, 30));
        var path = doc.RootElement.GetProperty("stdout").GetString()!.Trim();
        Assert.Equal(Path.GetFullPath(parent), Path.GetFullPath(path));
        Assert.Equal(Path.GetFullPath(parent), Path.GetFullPath(doc.RootElement.GetProperty("cwd").GetString()!));
    }

    [Fact]
    public void Cmd_cd_persists_on_tab()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var h = new ShellHabitat();
        var start = Path.GetTempPath();
        var defaults = new ShellCwdDefaults { ProjectRoot = start };
        var parent = Path.GetFullPath(Path.Combine(start, ".."));
        _ = h.Run(defaults, "cd /d ..", "cmdnav", null, "cmd", 30);
        using var doc = JsonDocument.Parse(h.Run(defaults, "echo %CD%", "cmdnav", null, "cmd", 30));
        var path = doc.RootElement.GetProperty("stdout").GetString()!.Trim();
        Assert.Equal(Path.GetFullPath(parent), Path.GetFullPath(path));
        Assert.Equal(Path.GetFullPath(parent), Path.GetFullPath(doc.RootElement.GetProperty("cwd").GetString()!));
    }

    [Fact]
    public void Utf8_cyrillic_roundtrips_on_stdout()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var echo = OperatingSystem.IsWindows()
            ? "Write-Output 'привет-кириллица'"
            : "printf '%s\\n' 'привет-кириллица'";
        using var doc = JsonDocument.Parse(h.Run(defaults, echo, "utf", null, null, 30));
        Assert.Equal(ShellHabitat.Utf8CodePage, doc.RootElement.GetProperty("codepage").GetInt32());
        Assert.Contains("привет-кириллица", doc.RootElement.GetProperty("stdout").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Codepage_sticks_on_tab()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        _ = h.Run(defaults, "Write-Output ok", "cp", null, null, 30, codepage: 866);
        using var which = JsonDocument.Parse(h.Which("cp"));
        Assert.Equal(866, which.RootElement.GetProperty("codepage").GetInt32());
    }

    [Fact]
    public void Env_var_persists_on_tab()
    {
        // Env-delta peel currently implemented for pwsh host wrap; cmd/unix later.
        if (!OperatingSystem.IsWindows())
            return;

        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        _ = h.Run(defaults, "$env:CDP_DOG_ENV='sticky-v1'; Write-Output $env:CDP_DOG_ENV", "envtab", null, null, 30);
        using var doc = JsonDocument.Parse(h.Run(defaults, "Write-Output $env:CDP_DOG_ENV", "envtab", null, null, 30));
        Assert.Contains("sticky-v1", doc.RootElement.GetProperty("stdout").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Cmd_cd_with_spaces_persists()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var spaced = Path.Combine(Path.GetTempPath(), "tm habitat spaces " + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(spaced);
        try
        {
            var h = new ShellHabitat();
            var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
            _ = h.Run(defaults, $"cd /d \"{spaced}\"", "spc", null, "cmd", 30);
            using var doc = JsonDocument.Parse(h.Run(defaults, "echo %CD%", "spc", null, "cmd", 30));
            var path = doc.RootElement.GetProperty("stdout").GetString()!.Trim();
            Assert.Equal(Path.GetFullPath(spaced), Path.GetFullPath(path));
        }
        finally
        {
            try { Directory.Delete(spaced, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void History_indexes_are_non_negative()
    {
        var h = new ShellHabitat();
        var defaults = new ShellCwdDefaults { ProjectRoot = Path.GetTempPath() };
        var echo = OperatingSystem.IsWindows() ? "Write-Output x" : "echo x";
        _ = h.Run(defaults, echo, "h", null, null, 30);
        _ = h.Run(defaults, echo, "h", null, null, 30);
        using var doc = JsonDocument.Parse(h.History("h", 20));
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            Assert.True(item.GetProperty("index").GetInt32() >= 0);
    }
}
