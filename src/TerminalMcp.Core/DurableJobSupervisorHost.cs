#nullable enable
using System.Diagnostics;

namespace TerminalMcp.Core;

/// <summary>Spawn or nudge the out-of-process job supervisor (ADR-0032 layer 3b).</summary>
public static class DurableJobSupervisorHost
{
    public const string WatchArg = "--watch";

    public static string ExeName => DurableHostPaths.SupervisorBinaryName;

    public static bool TryEnsureRunning()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TERMINAL_JOB_SUPERVISOR"), "0", StringComparison.Ordinal))
            return false;

        var exe = ResolveExePath();
        if (exe is null)
            return false;

        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = WatchArg,
                UseShellExecute = false,
                CreateNoWindow = OperatingSystem.IsWindows(),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            });
            if (proc is null)
                return false;
            Thread.Sleep(400);
            proc.Refresh();
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public static string? ResolveExePath() => DurableHostPaths.ResolveSupervisorExe();

    public static string? ResolveCdpMcpExe(string? workerExeHint = null) =>
        DurableHostPaths.ResolveCdpMcpExe(workerExeHint);

    /// <summary>
    /// Lifecycle jobs must fire Composer CDT via CdpMcp — not habitat-only <see cref="Cdp.Ignite.Client.IgniteArmStore"/>.
    /// </summary>
    public static bool TryNotifyIgniteViaCdp(
        DurableLifecyclePayload? lifecycle,
        string igniteEvent,
        bool ok,
        string? pulse,
        string? detail,
        string? armId = null,
        int waitMs = 120_000)
    {
        var cdp = ResolveCdpMcpExe(lifecycle?.WorkerExePath);
        if (cdp is null)
            return false;

        var seat = lifecycle?.IgniteSeat ?? DurableHostPaths.DeriveIgniteSeat(lifecycle?.WorkerExePath);
        var argv = new List<string> { "--ignite-notify", igniteEvent, ok ? "--ok" : "--fail" };
        if (!string.IsNullOrWhiteSpace(pulse))
        {
            argv.Add("--pulse");
            argv.Add(pulse);
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            argv.Add("--detail");
            argv.Add(detail);
        }

        if (!string.IsNullOrWhiteSpace(seat))
        {
            argv.Add("--seat");
            argv.Add(seat);
        }

        if (!string.IsNullOrWhiteSpace(armId))
        {
            argv.Add("--arm-id");
            argv.Add(armId);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cdp,
                Arguments = string.Join(' ', argv.Select(QuoteArg)),
                UseShellExecute = false,
                CreateNoWindow = OperatingSystem.IsWindows(),
                WorkingDirectory = Path.GetDirectoryName(cdp) ?? Environment.CurrentDirectory
            };
            if (!string.IsNullOrWhiteSpace(seat))
                psi.Environment["CDP_IGNITE_SEAT"] = seat!;
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;
            proc.WaitForExit(Math.Max(1_000, waitMs));
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    static string QuoteArg(string arg) =>
        arg.Contains(' ') || arg.Contains('"')
            ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : arg;
}
