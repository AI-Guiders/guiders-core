using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TerminalMcp.Core;

/// <summary>
/// Agent terminal habitat: named tabs, real system shells, history/rerun, scene map (kj-1312 / ADR 0180).
/// One-shot by default; background=true keeps process alive — poll via scene/last, stop via kill.
/// Shared by CDP <c>cdp_shell_*</c> and sibling <c>terminal-mcp</c> (kj-1358).
/// </summary>
public sealed class ShellHabitat
{
    public const int Utf8CodePage = 65001;
    public const int MaxHistory = 50;
    public const int DefaultTimeoutSeconds = 60;
    public const int ScenePreviewChars = 160;
    /// <summary>Default body in shell_run/v0 (agent context tax). Use last max_chars= or include_raw for more.</summary>
    public const int AgentBodyChars = 2_000;
    public const int LastBodyChars = 12_000;
    public const int LiveBufferChars = 200_000;

    private readonly ConcurrentDictionary<string, Tab> _tabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>Fires after foreground exit or background waiter completes (not kill-only).</summary>
    public event Action<ShellFinishedInfo>? Finished;

    public string Scene()
    {
        if (_tabs.IsEmpty)
            EnsureMain();
        var tabs = _tabs.Values
            .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.ToSceneRow())
            .ToList();
        return JsonSerializer.Serialize(new
        {
            schema = "shell_scene/v0",
            ok = true,
            tab_count = tabs.Count,
            tabs
        }, Pretty);
    }

    /// <summary>
    /// After project open: pin the default tab cwd to session root so shell follows IDE, not a sticky dogfood folder.
    /// Explicit <c>cwd=</c> on a later run still overrides and sticks on that tab.
    /// </summary>
    public void SyncSessionCwd(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return;
        var full = Path.GetFullPath(projectRoot.Trim());
        if (!Directory.Exists(full))
            return;

        lock (_gate)
        {
            EnsureMain();
            if (_tabs.TryGetValue("main", out var main))
                main.Cwd = full;
        }
    }

    public string Which(string? tabId)
    {
        var tab = GetOrCreate(tabId);
        var shell = ResolveShell(tab.ShellPrefer);
        return JsonSerializer.Serialize(new
        {
            schema = "shell_which/v0",
            ok = true,
            tab = tab.Id,
            shell_kind = shell.Kind,
            shell_exe = shell.Exe,
            cwd = tab.Cwd,
            state = tab.State,
            pid = tab.Pid,
            codepage = tab.Codepage
        }, Pretty);
    }

    public string History(string? tabId, int n)
    {
        var tab = GetOrCreate(tabId);
        n = Math.Clamp(n <= 0 ? 20 : n, 1, MaxHistory);
        var items = tab.History.TakeLast(n).Select((h, i) => new
        {
            index = tab.History.Count - n + i,
            h.Command,
            h.Cwd,
            h.ExitCode,
            h.ShellKind,
            h.TsUtc,
            preview = Cap(h.Stdout + h.Stderr, ScenePreviewChars)
        }).ToList();
        return JsonSerializer.Serialize(new
        {
            schema = "shell_history/v0",
            ok = true,
            tab = tab.Id,
            count = items.Count,
            items
        }, Pretty);
    }

    public string Last(string? tabId, int maxChars)
    {
        var tab = GetOrCreate(tabId);
        maxChars = Math.Clamp(maxChars <= 0 ? AgentBodyChars : maxChars, 256, 200_000);

        if (tab.State == "running")
        {
            var (stdout, stderr) = tab.SnapshotLive();
            return JsonSerializer.Serialize(new
            {
                schema = "shell_last/v0",
                ok = true,
                tab = tab.Id,
                empty = false,
                state = "running",
                background = true,
                pid = tab.Pid,
                command = tab.LastCommand,
                cwd = tab.Cwd,
                started_utc = tab.StartedUtc,
                stdout = CapTail(stdout, maxChars),
                stderr = CapTail(stderr, maxChars),
                truncated = stdout.Length + stderr.Length > maxChars
            }, Pretty);
        }

        if (tab.Last is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = "shell_last/v0",
                ok = true,
                tab = tab.Id,
                empty = true,
                state = tab.State
            }, Pretty);
        }

        var h = tab.Last;
        return JsonSerializer.Serialize(new
        {
            schema = "shell_last/v0",
            ok = true,
            tab = tab.Id,
            empty = false,
            state = tab.State,
            index = tab.History.Count - 1,
            command = h.Command,
            cwd = h.Cwd,
            exit_code = h.ExitCode,
            shell_kind = h.ShellKind,
            ts_utc = h.TsUtc,
            stdout = CapTail(h.Stdout, maxChars),
            stderr = CapTail(h.Stderr, maxChars),
            truncated = h.Stdout.Length + h.Stderr.Length > maxChars
        }, Pretty);
    }

    public string Run(
        ShellCwdDefaults? defaults,
        string? command,
        string? tabId,
        string? cwd,
        string? shellPrefer,
        int? timeoutSeconds,
        bool background = false,
        int? codepage = null,
        IReadOnlyList<string>? argv = null)
    {
        if (argv is { Count: > 0 })
            command = string.Join(' ', argv.Select(QuotePwshArg));

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("command or argv is required.");

        var tab = GetOrCreate(tabId);
        if (codepage is { } cp)
            tab.Codepage = cp;
        if (shellPrefer is { Length: > 0 })
            tab.ShellPrefer = shellPrefer.Trim();

        var work = ResolveCwd(cwd, tab, defaults ?? ShellCwdDefaults.Empty);
        tab.Cwd = work;
        return background
            ? StartBackground(tab, command.Trim(), work)
            : ExecuteForeground(tab, command.Trim(), work, timeoutSeconds);
    }

    static string QuotePwshArg(string a)
    {
        if (a.Length == 0) return "''";
        if (a.IndexOfAny([' ', '\'', '"', '`', '$']) < 0)
            return a;
        return "'" + a.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public string Rerun(
        ShellCwdDefaults? defaults,
        string? tabId,
        int? index,
        int? timeoutSeconds,
        bool background = false)
    {
        var tab = GetOrCreate(tabId);
        if (tab.History.Count == 0)
            throw new InvalidOperationException($"Tab '{tab.Id}' has empty history.");

        HistoryEntry entry;
        if (index is null || index < 0)
            entry = tab.History[^1];
        else
        {
            if (index.Value >= tab.History.Count)
                throw new ArgumentException($"history index {index} out of range (0..{tab.History.Count - 1}).");
            entry = tab.History[index.Value];
        }

        var work = Directory.Exists(entry.Cwd) ? entry.Cwd : ResolveCwd(null, tab, defaults ?? ShellCwdDefaults.Empty);
        tab.Cwd = work;
        return background
            ? StartBackground(tab, entry.Command, work)
            : ExecuteForeground(tab, entry.Command, work, timeoutSeconds);
    }

    public string Kill(string? tabId)
    {
        var tab = GetOrCreate(tabId);
        if (tab.Running is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = "shell_kill/v0",
                ok = true,
                tab = tab.Id,
                killed = false,
                reason = "not_running",
                state = tab.State
            }, Pretty);
        }

        var pid = tab.Pid;
        try { tab.Running.Kill(entireProcessTree: true); } catch { /* ignore */ }
        tab.Waiter?.Wait(TimeSpan.FromSeconds(5));
        return JsonSerializer.Serialize(new
        {
            schema = "shell_kill/v0",
            ok = true,
            tab = tab.Id,
            killed = true,
            pid,
            state = tab.State,
            exit_code = tab.Last?.ExitCode
        }, Pretty);
    }

    public string Close(string? tabId)
    {
        var id = string.IsNullOrWhiteSpace(tabId) ? "main" : tabId.Trim();
        if (!_tabs.TryGetValue(id, out var tab))
        {
            return JsonSerializer.Serialize(new
            {
                schema = "shell_close/v0",
                ok = true,
                tab = id,
                closed = false,
                reason = "missing"
            }, Pretty);
        }

        if (tab.Running is not null)
        {
            try { tab.Running.Kill(entireProcessTree: true); } catch { /* ignore */ }
            tab.Waiter?.Wait(TimeSpan.FromSeconds(5));
        }

        _tabs.TryRemove(id, out _);
        return JsonSerializer.Serialize(new
        {
            schema = "shell_close/v0",
            ok = true,
            tab = id,
            closed = true,
            tab_count = _tabs.Count
        }, Pretty);
    }

    private string ExecuteForeground(Tab tab, string command, string cwd, int? timeoutSeconds)
    {
        EnsureIdle(tab);
        var shell = ResolveShell(tab.ShellPrefer);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? DefaultTimeoutSeconds, 1, 600));
        tab.State = "running";
        tab.LastCommand = command;
        tab.StartedUtc = DateTime.UtcNow;
        tab.ClearLive();
        try
        {
            using var p = StartProcess(shell, command, cwd);
            tab.Attach(p);
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                tab.Detach();
                tab.State = "failed";
                throw new TimeoutException($"shell timed out after {timeout.TotalSeconds:0}s: {command}");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult() ?? "";
            var stderr = stderrTask.GetAwaiter().GetResult() ?? "";
            var entry = CommitHistory(tab, command, cwd, p.ExitCode, shell.Kind, stdout, stderr);
            tab.Detach();
            tab.State = p.ExitCode == 0 ? "idle" : "failed";
            RaiseFinished(tab.Id, command, cwd, p.ExitCode, background: false);
            return JsonResultRun(tab, command, cwd, shell, p.ExitCode, entry, background: false);
        }
        catch (TimeoutException)
        {
            tab.Detach();
            tab.State = "failed";
            throw new InvalidOperationException($"shell timed out after {timeout.TotalSeconds:0}s: {command}");
        }
        catch
        {
            tab.Detach();
            tab.State = "failed";
            throw;
        }
    }

    private string StartBackground(Tab tab, string command, string cwd)
    {
        EnsureIdle(tab);
        var shell = ResolveShell(tab.ShellPrefer);
        tab.State = "running";
        tab.LastCommand = command;
        tab.StartedUtc = DateTime.UtcNow;
        tab.ClearLive();

        var p = StartProcess(shell, command, cwd);
        tab.Attach(p);
        BeginCapture(tab, p);

        tab.Waiter = Task.Run(() =>
        {
            try
            {
                p.WaitForExit();
                var (stdout, stderr) = tab.SnapshotLive();
                CommitHistory(tab, command, cwd, p.ExitCode, shell.Kind, stdout, stderr);
                tab.State = p.ExitCode == 0 ? "idle" : "failed";
                RaiseFinished(tab.Id, command, cwd, p.ExitCode, background: true);
            }
            catch
            {
                tab.State = "failed";
            }
            finally
            {
                tab.Detach();
                try { p.Dispose(); } catch { /* ignore */ }
            }
        });

        return JsonSerializer.Serialize(new
        {
            schema = "shell_run/v0",
            ok = true,
            tab = tab.Id,
            command,
            cwd,
            shell_kind = shell.Kind,
            shell_exe = shell.Exe,
            background = true,
            state = "running",
            pid = tab.Pid,
            started_utc = tab.StartedUtc,
            hint = "Poll cdp_shell_scene / cdp_shell_last; stop with cdp_shell_kill."
        }, Pretty);
    }

    private static void EnsureIdle(Tab tab)
    {
        if (tab.Running is not null || tab.State == "running")
            throw new InvalidOperationException(
                $"Tab '{tab.Id}' is running (pid={tab.Pid}). cdp_shell_kill or wait via scene/last.");
    }

    private static HistoryEntry CommitHistory(
        Tab tab,
        string command,
        string cwd,
        int exit,
        string shellKind,
        string stdout,
        string stderr)
    {
        var entry = new HistoryEntry(command, cwd, exit, shellKind, DateTime.UtcNow, stdout, stderr);
        lock (tab.HistoryGate)
        {
            tab.History.Add(entry);
            while (tab.History.Count > MaxHistory)
                tab.History.RemoveAt(0);
            tab.Last = entry;
        }

        return entry;
    }

    void RaiseFinished(string tabId, string command, string cwd, int exitCode, bool background)
    {
        var handler = Finished;
        if (handler is null) return;
        try
        {
            handler(new ShellFinishedInfo(tabId, command, cwd, exitCode, background, DateTimeOffset.UtcNow));
        }
        catch
        {
            /* subscriber must not break shell */
        }
    }

    private static string JsonResultRun(
        Tab tab,
        string command,
        string cwd,
        ShellSpec shell,
        int exit,
        HistoryEntry entry,
        bool background) =>
        JsonSerializer.Serialize(new
        {
            schema = "shell_run/v0",
            ok = exit == 0,
            tab = tab.Id,
            command,
            cwd,
            shell_kind = shell.Kind,
            shell_exe = shell.Exe,
            background,
            exit_code = exit,
            codepage = tab.Codepage,
            stdout = CapTail(entry.Stdout, AgentBodyChars),
            stderr = CapTail(entry.Stderr, AgentBodyChars),
            history_index = tab.History.Count - 1,
            truncated = entry.Stdout.Length + entry.Stderr.Length > AgentBodyChars,
            hint = entry.Stdout.Length + entry.Stderr.Length > AgentBodyChars
                ? "Body capped for context tax — cdp_shell_last max_chars=12000 for fuller tail"
                : null
        }, Pretty);

    private static void BeginCapture(Tab tab, Process p)
    {
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            tab.AppendLive(stdout: e.Data + Environment.NewLine, stderr: null);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            tab.AppendLive(stdout: null, stderr: e.Data + Environment.NewLine);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
    }

    private Tab GetOrCreate(string? tabId)
    {
        var id = string.IsNullOrWhiteSpace(tabId) ? "main" : tabId.Trim();
        if (!IsSafeTabId(id))
            throw new ArgumentException("tab id: use letters, digits, _- (max 32).");

        if (_tabs.TryGetValue(id, out var existing))
            return existing;

        lock (_gate)
        {
            if (_tabs.TryGetValue(id, out existing))
                return existing;
            var tab = new Tab(id);
            _tabs[id] = tab;
            return tab;
        }
    }

    private void EnsureMain() => GetOrCreate("main");

    private static string ResolveCwd(string? cwd, Tab tab, ShellCwdDefaults defaults)
    {
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            var full = Path.GetFullPath(cwd.Trim());
            if (!Directory.Exists(full))
                throw new ArgumentException($"cwd does not exist: {full}");
            return full;
        }

        if (!string.IsNullOrWhiteSpace(tab.Cwd) && Directory.Exists(tab.Cwd))
            return tab.Cwd;

        var fallback = defaults.ProjectRoot ?? defaults.ScmRoot ?? Environment.CurrentDirectory;
        return Path.GetFullPath(fallback);
    }

    private static bool IsSafeTabId(string id) =>
        id.Length is > 0 and <= 32 && Regex.IsMatch(id, "^[A-Za-z0-9_-]+$");

    private static string Cap(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + $"\n…(+{s.Length - max} chars)";
    }

    /// <summary>Prefer tail — agent cares about recent output (leather terminal).</summary>
    private static string CapTail(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return $"…(+{s.Length - max} chars)\n" + s[^max..];
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private readonly record struct ShellSpec(string Kind, string Exe);

    private static ShellSpec ResolveShell(string? prefer)
    {
        prefer = prefer?.Trim().ToLowerInvariant();
        if (OperatingSystem.IsWindows())
        {
            if (prefer is "cmd" or "cmd.exe")
                return new("cmd", "cmd.exe");

            foreach (var cand in new[] { "pwsh.exe", "pwsh", "powershell.exe", "powershell" })
            {
                if (TryFindOnPath(cand, out var path))
                    return new("pwsh", path);
            }

            return new("cmd", "cmd.exe");
        }

        var unix = prefer is { Length: > 0 } && prefer is not "pwsh" and not "cmd"
            ? prefer
            : (Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } sh ? sh : "/bin/bash");
        var kind = Path.GetFileName(unix);
        return new(kind, unix);
    }

    private static void ApplyFreshPath(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var path = ComposePathEnv();
        Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.Process);
        psi.Environment["PATH"] = path;
    }

    static string ComposePathEnv()
    {
        if (!OperatingSystem.IsWindows())
            return Environment.GetEnvironmentVariable("PATH") ?? "";

        var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var process = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process) ?? "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        void Add(string block)
        {
            foreach (var raw in block.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = raw.Trim();
                if (dir.Length == 0 || !seen.Add(dir))
                    continue;
                parts.Add(dir);
            }
        }

        Add(machine);
        Add(user);
        Add(process);
        return string.Join(Path.PathSeparator, parts);
    }

    private static bool TryFindOnPath(string name, out string full)
    {
        full = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
                ArgumentList = { name },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            ApplyFreshPath(psi);
            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line) || !File.Exists(line.Trim()))
                return false;
            full = line.Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Process StartProcess(ShellSpec shell, string command, string cwd)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ApplyFreshPath(psi);

        if (OperatingSystem.IsWindows() && shell.Kind == "cmd")
        {
            psi.FileName = shell.Exe;
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else if (OperatingSystem.IsWindows())
        {
            psi.FileName = shell.Exe;
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.FileName = shell.Exe;
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add(command);
        }

        var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {shell.Exe}");
        p.StandardInput.Close();
        return p;
    }

    private sealed class Tab(string id)
    {
        public string Id { get; } = id;
        public string Cwd { get; set; } = "";
        public string? ShellPrefer { get; set; }
        public int Codepage { get; set; } = Utf8CodePage;
        public string State { get; set; } = "idle";
        public string? LastCommand { get; set; }
        public HistoryEntry? Last { get; set; }
        public List<HistoryEntry> History { get; } = [];
        public object HistoryGate { get; } = new();
        public Process? Running { get; private set; }
        public int? Pid { get; private set; }
        public DateTime? StartedUtc { get; set; }
        public Task? Waiter { get; set; }
        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();
        private readonly object _liveGate = new();

        public void Attach(Process p)
        {
            Running = p;
            Pid = p.Id;
        }

        public void Detach()
        {
            Running = null;
            Pid = null;
            Waiter = null;
            StartedUtc = null;
        }

        public void ClearLive()
        {
            lock (_liveGate)
            {
                _stdout.Clear();
                _stderr.Clear();
            }
        }

        public void AppendLive(string? stdout, string? stderr)
        {
            lock (_liveGate)
            {
                if (stdout is not null)
                {
                    _stdout.Append(stdout);
                    TrimFront(_stdout);
                }

                if (stderr is not null)
                {
                    _stderr.Append(stderr);
                    TrimFront(_stderr);
                }
            }
        }

        public (string Stdout, string Stderr) SnapshotLive()
        {
            lock (_liveGate)
                return (_stdout.ToString(), _stderr.ToString());
        }

        private static void TrimFront(StringBuilder sb)
        {
            if (sb.Length <= LiveBufferChars)
                return;
            sb.Remove(0, sb.Length - LiveBufferChars);
        }

        public object ToSceneRow()
        {
            var shell = ResolveShell(ShellPrefer);
            string? preview;
            if (State == "running")
            {
                var (so, se) = SnapshotLive();
                preview = CapTail((so + se).Trim(), ScenePreviewChars);
            }
            else
            {
                preview = Last is null ? null : CapTail((Last.Stdout + Last.Stderr).Trim(), ScenePreviewChars);
            }

            return new
            {
                id = Id,
                state = State,
                shell_kind = shell.Kind,
                cwd = string.IsNullOrEmpty(Cwd) ? null : Cwd,
                last_command = LastCommand,
                last_exit = Last?.ExitCode,
                pid = Pid,
                started_utc = StartedUtc,
                preview,
                history_count = History.Count
            };
        }
    }

    private sealed record HistoryEntry(
        string Command,
        string Cwd,
        int ExitCode,
        string ShellKind,
        DateTime TsUtc,
        string Stdout,
        string Stderr);
}
