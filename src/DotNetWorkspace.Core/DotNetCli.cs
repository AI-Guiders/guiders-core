using System.Diagnostics;

namespace DotNetWorkspace.Core;

internal static class DotNetCli
{
    public static int Restore(string projectPath) =>
        Run("restore", projectPath);

    public static int Build(string projectPath) =>
        Run("build", projectPath);

    static int Run(string verb, string projectPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{verb} \"{projectPath}\" -nologo -v:q",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc is null)
            return -1;

        proc.WaitForExit();
        return proc.ExitCode;
    }
}
