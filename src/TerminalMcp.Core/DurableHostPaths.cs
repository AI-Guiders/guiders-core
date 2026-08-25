#nullable enable

namespace TerminalMcp.Core;

/// <summary>RID-aware install/binary resolution for durable jobs (ADR-0032).</summary>
public static class DurableHostPaths
{
    public static string CdpMcpBinaryName =>
        OperatingSystem.IsWindows() ? "CdpMcp.exe" : "CdpMcp";

    public static string SupervisorBinaryName =>
        OperatingSystem.IsWindows() ? "TerminalMcp.Supervisor.exe" : "TerminalMcp.Supervisor";

    public static string? ResolveCdpMcpExe(string? workerExeHint = null)
    {
        if (TryExistingFile(workerExeHint, out var hinted))
            return hinted;

        var fromEnv = Environment.GetEnvironmentVariable("CDP_MCP_EXE");
        if (TryExistingFile(fromEnv, out var envHit))
            return envHit;

        var sibling = Path.Combine(AppContext.BaseDirectory, CdpMcpBinaryName);
        if (File.Exists(sibling))
            return sibling;

        foreach (var root in EnumerateCdpInstallRoots())
        {
            var candidate = Path.Combine(root, CdpMcpBinaryName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static string? ResolveSupervisorExe()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TERMINAL_MCP_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var candidate = Path.Combine(fromEnv, SupervisorBinaryName);
            if (File.Exists(candidate))
                return candidate;
        }

        var sibling = Path.Combine(AppContext.BaseDirectory, SupervisorBinaryName);
        if (File.Exists(sibling))
            return sibling;

        if (OperatingSystem.IsWindows())
        {
            var winDeploy = Path.Combine(@"D:\terminal-mcp", SupervisorBinaryName);
            if (File.Exists(winDeploy))
                return winDeploy;
        }

        foreach (var rid in new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" })
        {
            var dev = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "terminal-mcp-supervisor", "bin", "Debug", "net10.0", rid, SupervisorBinaryName));
            if (File.Exists(dev))
                return dev;
        }

        foreach (var baseRoot in EnumerateAiguidersRoots())
        {
            var candidate = Path.Combine(baseRoot, "terminal-mcp", SupervisorBinaryName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static IEnumerable<string> EnumerateCdpInstallRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return @"D:\cdp-mcp";
            yield return @"D:\cdp-mcp-debug";
        }

        var seatEnv = Environment.GetEnvironmentVariable("CDP_MCP_HOME");
        if (!string.IsNullOrWhiteSpace(seatEnv))
            yield return seatEnv;

        foreach (var baseRoot in EnumerateAiguidersRoots())
        {
            yield return Path.Combine(baseRoot, "cdp");
            yield return Path.Combine(baseRoot, "cdp-debug");
        }
    }

    public static IEnumerable<string> EnumerateAiguidersRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(lad))
                yield return Path.Combine(lad, "AIGuiders");
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                yield return Path.Combine(home, "Library", "Application Support", "AIGuiders");
            yield break;
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
            yield return Path.Combine(xdg, "AIGuiders");
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
                yield return Path.Combine(home, ".local", "share", "AIGuiders");
        }
    }

    /// <summary>
    /// Maps a worker binary path to ignite seat id (<c>cdp</c> | <c>cdp-debug</c>).
    /// Accepts install paths from any OS (Win/Mac/Linux); uses both-separator split, not <see cref="Path"/> APIs alone,
    /// because <c>Path.GetDirectoryName</c> ignores <c>\</c> on Unix and would drop foreign literals (e.g. CI parsing <c>D:\cdp-mcp\…</c>).
    /// </summary>
    public static string? DeriveIgniteSeat(string? workerExePath)
    {
        if (string.IsNullOrWhiteSpace(workerExePath))
            return null;

        if (TryDeriveSeatFromPathLiteral(workerExePath, out var seat))
            return seat;

        try
        {
            var full = Path.GetFullPath(workerExePath);
            if (!string.Equals(full, workerExePath, StringComparison.Ordinal)
                && TryDeriveSeatFromPathLiteral(full, out seat))
                return seat;
        }
        catch (ArgumentException)
        {
            // invalid path on this host
        }

        return null;
    }

    static bool TryDeriveSeatFromPathLiteral(string path, out string? seat)
    {
        foreach (var segment in EnumerateDirectorySegmentsNearestFirst(path))
        {
            if (InstallFolderToSeat.TryGetValue(segment, out seat))
                return true;
        }

        seat = null;
        return false;
    }

    static IEnumerable<string> EnumerateDirectorySegmentsNearestFirst(string path)
    {
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            yield break;

        for (var i = parts.Length - 2; i >= 0; i--)
            yield return parts[i];
    }

    static readonly Dictionary<string, string> InstallFolderToSeat =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cdp-mcp-debug"] = "cdp-debug",
            ["cdp-debug"] = "cdp-debug",
            ["cdp-mcp"] = "cdp",
            ["cdp"] = "cdp",
        };

    static bool TryExistingFile(string? path, out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            return false;
        fullPath = full;
        return true;
    }
}
