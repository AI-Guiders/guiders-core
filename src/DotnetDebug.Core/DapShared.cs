namespace DotnetDebug.Core;

/// <summary>Общие хелперы DAP для IDE и dotnet-debug-mcp.</summary>
public static class DapShared
{
    public const int DefaultRetryCount = 3;
    public const int DefaultRetryDelayMs = 250;

    /// <summary>Некоторые сборки netcoredbg отклоняют setExceptionBreakpoints — тогда false.</summary>
    public static async Task<bool> TrySetUnhandledExceptionBreakpointsAsync(DapClient client)
    {
        try
        {
            await client.SetExceptionBreakpointsAsync(["unhandled"]).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool IsTransientDapError(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("running", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("0x80004005", StringComparison.Ordinal)
            || msg.Contains("Failed command", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task WithRetryVoidAsync(Func<Task> action, int maxAttempts = DefaultRetryCount, int delayMs = DefaultRetryDelayMs)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (i < maxAttempts - 1 && IsTransientDapError(ex))
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }
    }

    public static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, int maxAttempts = DefaultRetryCount, int delayMs = DefaultRetryDelayMs)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (i < maxAttempts - 1 && IsTransientDapError(ex))
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Путь к исходнику для DAP setBreakpoints.</summary>
    public static string ResolveBreakpointFilePath(string workspaceRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Path.GetFullPath(filePath);
        var trimmed = filePath.Trim();
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);
        return Path.GetFullPath(Path.Combine(workspaceRoot, trimmed));
    }
}
