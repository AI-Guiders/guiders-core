using System.Diagnostics;
using System.Text;

namespace DotNetBuildTest.Core;

/// <summary>Запуск <c>dotnet</c> с таймаутом, отменой и построчным логом.</summary>
public static class DotnetProcessRunner
{
    public static async Task<CommandExecutionResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        Action<string>? onLogLine,
        IReadOnlyDictionary<string, string>? supplementalEnvironmentVariables = null)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        DotnetProcessIoEncoding.ApplyUtf8(psi);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (supplementalEnvironmentVariables is not null)
        {
            foreach (var kv in supplementalEnvironmentVariables)
                psi.Environment[kv.Key] = kv.Value;
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet.");

        var sb = new StringBuilder(4096);
        var stdOutTask = PumpReaderAsync(process.StandardOutput, sb, onLogLine);
        var stdErrTask = PumpReaderAsync(process.StandardError, sb, onLogLine);

        var timedOut = false;
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested;
            cancelled = !timedOut;
            TryTerminate(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
        var output = sb.ToString();
        var reason = timedOut ? "timeout" : cancelled ? "cancelled" : null;
        return new CommandExecutionResult(process.ExitCode, output, timedOut, cancelled, reason);
    }

    private static async Task PumpReaderAsync(StreamReader reader, StringBuilder sb, Action<string>? onLogLine)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
                break;

            sb.AppendLine(line);
            onLogLine?.Invoke(line);
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }
}
