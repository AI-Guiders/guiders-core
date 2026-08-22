using System.Text.RegularExpressions;

namespace Cdp.Lsp;

/// <summary>
/// Resolve LSP launch command on PATH (incl. Windows .cmd npm shims → node + *.js for redirected stdio).
/// </summary>
public static class LspCommandResolver
{
    public sealed record ResolvedLaunch(string FileName, IReadOnlyList<string> Args, string Display);

    static readonly string[] PathExtFallback = [".exe", ".cmd", ".bat", ".com", ""];

    public static ResolvedLaunch Resolve(LspLaunchPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var candidates = preset.CommandCandidates is { Count: > 0 }
            ? preset.CommandCandidates
            : [preset.Command];

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            if (TryResolveOne(candidate.Trim(), preset.Args, out var hit))
                return hit;
        }

        throw new InvalidOperationException(
            $"lsp_server_missing: tried [{string.Join(", ", candidates)}] (language={preset.Id}). " +
            "Install via CDP Options: cdp_settings op=lsp_ensure id=" + preset.Id +
            " (or set [[languages.lsp]] command).");
    }

    static bool TryResolveOne(string command, IReadOnlyList<string> args, out ResolvedLaunch resolved)
    {
        resolved = null!;
        var found = FindOnPath(command);
        if (found is null)
            return false;

        var ext = Path.GetExtension(found);
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            if (TryRewriteNpmCmdToNode(found, args, out resolved))
                return true;
        }

        resolved = new ResolvedLaunch(found, args, command);
        return true;
    }

    /// <summary>
    /// npm global shims are .cmd; CreateProcess cannot run them with redirected stdio.
    /// Parse the shim for its real node script under node_modules (not a hardcoded package).
    /// </summary>
    static bool TryRewriteNpmCmdToNode(string cmdPath, IReadOnlyList<string> args, out ResolvedLaunch resolved)
    {
        resolved = null!;
        var npmDir = Path.GetDirectoryName(cmdPath);
        if (string.IsNullOrEmpty(npmDir))
            return false;

        if (!TryExtractNpmShimScript(cmdPath, npmDir, out var scriptPath))
            return false;

        var node = FindOnPath("node") ?? "node";
        var launchArgs = new List<string> { scriptPath };
        launchArgs.AddRange(args);
        resolved = new ResolvedLaunch(node, launchArgs, $"node {Path.GetFileName(scriptPath)}");
        return true;
    }

    /// <summary>
    /// npm Windows shims typically end with: "%_prog%" "%dp0%\\node_modules\\…\\script" %*
    /// </summary>
    internal static bool TryExtractNpmShimScript(string cmdPath, string npmDir, out string scriptPath)
    {
        scriptPath = null!;
        string text;
        try
        {
            text = File.ReadAllText(cmdPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        foreach (Match m in Regex.Matches(
                     text,
                     "\"%(?:~)?dp0%\\\\(?<rel>[^\"]+)\"",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var rel = m.Groups["rel"].Value.Replace('/', Path.DirectorySeparatorChar);
            if (rel.Equals("node.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!rel.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = Path.GetFullPath(Path.Combine(npmDir, rel));
            if (!File.Exists(candidate))
                continue;

            scriptPath = candidate;
            return true;
        }

        return false;
    }

    public static string? FindOnPath(string command)
    {
        if (Path.IsPathRooted(command) && File.Exists(command))
            return command;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pathext = Environment.GetEnvironmentVariable("PATHEXT");
        var exts = string.IsNullOrWhiteSpace(pathext)
            ? PathExtFallback
            : pathext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // If caller already gave an extension, try as-is first.
        var hasExt = Path.HasExtension(command);
        foreach (var dir in dirs)
        {
            if (hasExt)
            {
                var direct = Path.Combine(dir, command);
                if (File.Exists(direct))
                    return direct;
                continue;
            }

            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
