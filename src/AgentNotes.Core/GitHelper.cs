using System.Diagnostics;

namespace AgentNotes.Core;

/// <summary>
/// Wraps git CLI calls for inline versioning (ADR-027).
/// Git binary path comes from config <c>[git]path</c>; empty = resolve from PATH.
/// No fallback: failures surface to the caller, we never re-implement versioning.
/// </summary>
public static class GitHelper
{
    private static string _gitPath = "git";

    /// <summary>Effective git binary (path or "git" for PATH lookup).</summary>
    public static string GitPath => _gitPath;

    /// <summary>Set the git binary path (empty or null = use PATH). Call once at startup.</summary>
    public static void Configure(string? gitPath)
    {
        _gitPath = string.IsNullOrWhiteSpace(gitPath) ? "git" : gitPath.Trim();
    }

    /// <summary>Repo root for a directory, or null when not inside a git repository (or git missing).</summary>
    public static string? FindRepoRoot(string startDir)
    {
        var (exit, output) = Run(startDir, "rev-parse", "--show-toplevel");
        if (exit != 0)
            return null;
        var root = output.Trim().Split('\n')[0].Trim();
        return root.Length == 0 ? null : root;
    }

    /// <summary>True when the file has uncommitted changes (staged or working tree).</summary>
    public static bool IsFileDirty(string repoRoot, string relativePath)
    {
        var (exit, output) = Run(repoRoot, "status", "--porcelain", "--", relativePath.Replace('\\', '/'));
        return exit == 0 && output.Trim().Length > 0;
    }

    /// <summary>
    /// Stage the file and commit it (path-scoped commit, never <c>add -A</c>).
    /// "Nothing to commit" (content unchanged) is success with a null hash.
    /// </summary>
    public static bool TryCommit(string repoRoot, string relativePath, string message, out string? shortHash, out string detail)
    {
        shortHash = null;
        var rel = relativePath.Replace('\\', '/');

        var (addExit, addOutput) = Run(repoRoot, "add", "--", rel);
        if (addExit != 0)
        {
            detail = $"git add failed: {addOutput.Trim()}";
            return false;
        }

        var (commitExit, commitOutput) = Run(repoRoot, "commit", "-m", message, "--", rel);
        if (commitExit != 0)
        {
            if (commitOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
            {
                detail = "";
                return true;
            }

            detail = $"git commit failed: {commitOutput.Trim()}";
            return false;
        }

        var (hashExit, hashOutput) = Run(repoRoot, "rev-parse", "--short", "HEAD");
        shortHash = hashExit == 0 ? hashOutput.Trim() : null;
        detail = "";
        return true;
    }

    /// <summary>Push to the default remote (periodic tick or explicit operator command, not per-write).</summary>
    public static bool Push(string repoRoot, out string detail)
    {
        var (exit, output) = Run(repoRoot, new[] { "push" }, timeoutMs: 60_000);
        detail = output.Trim();
        return exit == 0;
    }

    /// <summary>Startup diagnostics (ADR-027): git runs, primary root is a repo, identity is configured.</summary>
    /// <returns>null when git is ready; otherwise an operator-facing problem description.</returns>
    public static string? StartupProblem(string primaryKnowledgeRoot)
    {
        var (verExit, verOutput) = Run(primaryKnowledgeRoot, "--version");
        if (verExit != 0)
            return $"git не найден по пути '{_gitPath}' ({verOutput.Trim()}). Укажите путь в [git]path конфига или установите git — ADR-027 без фолбэка на .revisions.";

        var root = FindRepoRoot(primaryKnowledgeRoot);
        if (root is null)
            return $"primary knowledge root '{primaryKnowledgeRoot}' не является git-репозиторием. git init — ADR-027: версионирование только через git.";

        var (nameExit, nameOutput) = Run(primaryKnowledgeRoot, "config", "user.name");
        if (nameExit != 0 || string.IsNullOrWhiteSpace(nameOutput))
            return $"в '{root}' не настроен git identity: git config user.name / user.email — без него commit упадёт.";

        return null;
    }

    private static (int exitCode, string output) Run(string workingDir, params string[] arguments)
    {
        var timeoutMs = arguments.Length > 0 && arguments[0] == "push" ? 60_000 : 15_000;
        return Run(workingDir, arguments, timeoutMs);
    }

    private static (int exitCode, string output) Run(string workingDir, string[] arguments, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _gitPath,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);
            // Non-interactive: never prompt for credentials
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "Failed to start git process");

            process.StandardInput.Close();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return (-1, $"git timeout after {timeoutMs}ms: git {string.Join(' ', arguments)}");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
