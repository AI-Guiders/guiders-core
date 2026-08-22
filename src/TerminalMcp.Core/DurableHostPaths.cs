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

    public static string? DeriveIgniteSeat(string? workerExePath)
    {
        if (string.IsNullOrWhiteSpace(workerExePath))
            return null;
        var root = Path.GetDirectoryName(Path.GetFullPath(workerExePath));
        if (string.IsNullOrWhiteSpace(root))
            return null;
        var leaf = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return leaf switch
        {
            "cdp-mcp-debug" => "cdp-debug",
            "cdp-mcp" => "cdp",
            _ => null
        };
    }

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
