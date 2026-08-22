using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetDebug.Core;

/// <summary>Минимальный DAP-клиент: обмен с debug adapter (netcoredbg) по stdio. Content-Length + JSON-RPC.</summary>
public sealed class DapClient : IAsyncDisposable
{
    private static readonly Regex ContentLengthRegex = new(@"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
    private readonly Stream _writer;
    private readonly Stream _reader;
    private readonly Process _process;
    private int _requestId;
    private readonly byte[] _buffer = new byte[1024 * 64];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ConcurrentDictionary<int, TaskCompletionSource<DapResponseResult>> _pendingResponses = new();
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;
    private int _disposeState;

    private sealed record DapResponseResult(bool Success, string? ErrorMessage, JsonElement? Body);

    /// <summary>Вызывается при получении события от адаптера (например stopped).</summary>
    public Action<string, JsonElement>? OnEvent { get; set; }

    /// <summary>Вызывается при обрыве связи.</summary>
    public Action? OnConnectionLost { get; set; }

    private DapClient(Process process, Stream reader, Stream writer)
    {
        _process = process;
        _reader = reader;
        _writer = writer;
    }

    private async Task RunReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var raw = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : "";
                if (type == "event" && root.TryGetProperty("event", out var ev) && root.TryGetProperty("body", out var eventBody))
                {
                    OnEvent?.Invoke(ev.GetString() ?? "", eventBody);
                    continue;
                }
                if (type == "response" && root.TryGetProperty("request_seq", out var seqEl))
                {
                    var requestSeq = seqEl.GetInt32();
                    var success = root.TryGetProperty("success", out var succ) && succ.GetBoolean();
                    var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                    JsonElement? body = null;
                    if (root.TryGetProperty("body", out var b))
                        body = b.Clone();
                    var result = new DapResponseResult(success, message, body);
                    if (_pendingResponses.TryRemove(requestSeq, out var tcs))
                        tcs.TrySetResult(result);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsConnectionLost(ex))
        {
            foreach (var kv in _pendingResponses)
                kv.Value.TrySetResult(new DapResponseResult(false, ex.Message, null));
            OnConnectionLost?.Invoke();
        }
    }

    private static bool IsConnectionLost(Exception ex)
    {
        return ex is IOException or EndOfStreamException or ObjectDisposedException
            || (ex is InvalidOperationException && ex.Message.Contains("stream ended", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Запускает netcoredbg с --interpreter=vscode.</summary>
    public static Task<DapClient> StartAsync(
        string netcoredbgPath,
        CancellationToken cancellationToken = default,
        string clientId = "dotnet-debug",
        string clientName = "DotnetDebug") =>
        StartAdapterAsync(new ProcessStartInfo
        {
            FileName = netcoredbgPath,
            Arguments = "--interpreter=vscode"
        }, "netcoredbg", cancellationToken, clientId, clientName);

    /// <summary>Generic DAP adapter over stdio (PSES debug, etc.).</summary>
    public static async Task<DapClient> StartAdapterAsync(
        ProcessStartInfo startInfo,
        string adapterId,
        CancellationToken cancellationToken = default,
        string clientId = "cdp-debug",
        string clientName = "CdpMcp")
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start DAP adapter: {startInfo.FileName}");
        var writer = process.StandardInput.BaseStream;
        var reader = process.StandardOutput.BaseStream;
        var client = new DapClient(process, reader, writer);
        client._readLoopCts = new CancellationTokenSource();
        client._readLoopTask = Task.Run(() => client.RunReadLoopAsync(client._readLoopCts.Token), CancellationToken.None);
        await client.SendRequestAsync("initialize", new Dictionary<string, object?>
        {
            ["clientId"] = clientId,
            ["clientName"] = clientName,
            ["adapterID"] = adapterId,
            ["pathFormat"] = "path",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["supportsVariableType"] = true,
            ["supportsRunInTerminalRequest"] = false
        }, cancellationToken).ConfigureAwait(false);
        return client;
    }

    public async Task SendRequestAsync(string method, object? args, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<DapResponseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[id] = tcs;
        try
        {
            var request = new Dictionary<string, object?>
            {
                ["seq"] = id,
                ["type"] = "request",
                ["command"] = method,
                ["arguments"] = args
            };
            var body = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            var header = Encoding.UTF8.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await _writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _writer.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            var result = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException($"DAP {method}: {result.ErrorMessage ?? "Unknown error"}");
        }
        finally
        {
            _pendingResponses.TryRemove(id, out _);
        }
    }

    public async Task<JsonElement?> SendRequestWithBodyAsync(string method, object? args, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<DapResponseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[id] = tcs;
        try
        {
            var request = new Dictionary<string, object?>
            {
                ["seq"] = id,
                ["type"] = "request",
                ["command"] = method,
                ["arguments"] = args
            };
            var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            var header = Encoding.UTF8.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
            await _writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _writer.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            var result = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException($"DAP {method}: {result.ErrorMessage ?? "Unknown error"}");
            return result.Body;
        }
        finally
        {
            _pendingResponses.TryRemove(id, out _);
        }
    }

    public Task ContinueAsync(int threadId, CancellationToken cancellationToken = default) =>
        SendRequestAsync("continue", new Dictionary<string, object?> { ["threadId"] = threadId }, cancellationToken);

    public Task NextAsync(int threadId, CancellationToken cancellationToken = default) =>
        SendRequestAsync("next", new Dictionary<string, object?> { ["threadId"] = threadId }, cancellationToken);

    public Task StepInAsync(int threadId, CancellationToken cancellationToken = default) =>
        SendRequestAsync("stepIn", new Dictionary<string, object?> { ["threadId"] = threadId }, cancellationToken);

    public Task StepOutAsync(int threadId, CancellationToken cancellationToken = default) =>
        SendRequestAsync("stepOut", new Dictionary<string, object?> { ["threadId"] = threadId }, cancellationToken);

    public Task<JsonElement?> StackTraceAsync(int threadId, int startFrame = 0, int levels = 20, CancellationToken cancellationToken = default) =>
        SendRequestWithBodyAsync("stackTrace", new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["startFrame"] = startFrame,
            ["levels"] = levels
        }, cancellationToken);

    public Task<JsonElement?> ScopesAsync(int frameId, CancellationToken cancellationToken = default) =>
        SendRequestWithBodyAsync("scopes", new Dictionary<string, object?> { ["frameId"] = frameId }, cancellationToken);

    /// <summary>Переменные по <c>variablesReference</c>. Для больших массивов см. перегрузку с <c>filter</c>/<c>start</c>/<c>count</c> (DAP paging).</summary>
    public Task<JsonElement?> VariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
        VariablesAsync(variablesReference, filter: null, start: null, count: null, cancellationToken);

    /// <summary>Запрос дочерних переменных с фильтром (например <c>indexed</c> / <c>named</c>) и страницей.</summary>
    public Task<JsonElement?> VariablesAsync(
        int variablesReference,
        string? filter,
        int? start,
        int? count,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["variablesReference"] = variablesReference };
        if (filter != null)
            args["filter"] = filter;
        if (start.HasValue)
            args["start"] = start.Value;
        if (count.HasValue)
            args["count"] = count.Value;
        return SendRequestWithBodyAsync("variables", args, cancellationToken);
    }

    public async Task LaunchAsync(
        string program,
        string? cwd = null,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var fullProgram = Path.GetFullPath(program);
        var arguments = new Dictionary<string, object?>
        {
            ["program"] = fullProgram,
            ["cwd"] = string.IsNullOrWhiteSpace(cwd) ? Path.GetDirectoryName(fullProgram) ?? fullProgram : Path.GetFullPath(cwd!)
        };
        if (args is { Count: > 0 })
            arguments["args"] = args;
        if (environment is { Count: > 0 })
        {
            var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in environment)
            {
                if (!string.IsNullOrEmpty(k))
                    env[k] = v;
            }

            if (env.Count > 0)
                arguments["env"] = env;
        }

        if (fullProgram.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            arguments["runtimeExecutable"] = "dotnet";
        await SendRequestAsync("launch", arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task LaunchPowerShellAsync(
        string scriptPath,
        string? cwd = null,
        IReadOnlyList<string>? args = null,
        CancellationToken cancellationToken = default)
    {
        var fullScript = Path.GetFullPath(scriptPath);
        var arguments = new Dictionary<string, object?>
        {
            ["request"] = "launch",
            ["type"] = "PowerShell",
            ["name"] = Path.GetFileName(fullScript),
            ["script"] = fullScript,
            ["cwd"] = string.IsNullOrWhiteSpace(cwd) ? Path.GetDirectoryName(fullScript) ?? fullScript : Path.GetFullPath(cwd!),
            ["createTemporaryIntegratedConsole"] = false,
            ["showDebuggerOnStart"] = true
        };
        if (args is { Count: > 0 })
            arguments["args"] = args;
        await SendRequestAsync("launch", arguments, cancellationToken).ConfigureAwait(false);
    }

    public Task AttachAsync(int processId, CancellationToken cancellationToken = default) =>
        SendRequestAsync("attach", new Dictionary<string, object?> { ["processId"] = processId }, cancellationToken);

    public async Task SetBreakpointsAsync(string sourcePath, IReadOnlyList<(int Line, string? Condition)> breakpoints, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(sourcePath);
        var bps = breakpoints.Select(b =>
        {
            var d = new Dictionary<string, object?> { ["line"] = b.Line };
            if (!string.IsNullOrEmpty(b.Condition))
                d["condition"] = b.Condition;
            return d;
        }).ToList();
        await SendRequestAsync("setBreakpoints", new Dictionary<string, object?>
        {
            ["source"] = new Dictionary<string, object?> { ["path"] = path },
            ["breakpoints"] = bps
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task SetExceptionBreakpointsAsync(IReadOnlyList<string> filters, CancellationToken cancellationToken = default) =>
        SendRequestAsync("setExceptionBreakpoints", new Dictionary<string, object?> { ["filters"] = filters }, cancellationToken);

    public Task ConfigurationDoneAsync(CancellationToken cancellationToken = default) =>
        SendRequestAsync("configurationDone", null, cancellationToken);

    public Task<JsonElement?> ThreadsAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithBodyAsync("threads", null, cancellationToken);

    private async Task<string> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var headerBuilder = new List<byte>(256);
        while (true)
        {
            var n = await _reader.ReadAsync(_buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new InvalidOperationException("DAP: stream ended.");
            headerBuilder.Add(_buffer[0]);
            if (headerBuilder.Count >= 4 &&
                headerBuilder[^4] == '\r' && headerBuilder[^3] == '\n' &&
                headerBuilder[^2] == '\r' && headerBuilder[^1] == '\n')
                break;
        }
        var headerStr = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(headerBuilder));
        var match = ContentLengthRegex.Match(headerStr);
        if (!match.Success)
            throw new InvalidOperationException("DAP: missing Content-Length in response.");
        var length = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var body = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await _reader.ReadAsync(body.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException("DAP: stream ended before message complete.");
            offset += read;
        }
        return Encoding.UTF8.GetString(body);
    }

    /// <summary>DAP <c>disconnect</c>: отцепить отладчик от целевого процесса и завершить адаптер. Не трогаем debuggee (<c>terminateDebuggee: false</c>).</summary>
    private async Task TryDisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await SendRequestAsync(
                "disconnect",
                new Dictionary<string, object?> { ["terminateDebuggee"] = false },
                cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException or ObjectDisposedException)
        {
            // Адаптер уже закрыт или отказал — идём в teardown.
        }
    }

    private void TryKillAdapterProcess()
    {
        try
        {
            if (_process.HasExited)
                return;
            _process.Kill(entireProcessTree: false);
            _process.WaitForExit(milliseconds: 2000);
        }
        catch (InvalidOperationException)
        {
            // процесс уже завершён
        }
        catch (Exception)
        {
            // Win32 / доступ — лучше не ронять Dispose
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            return;

        await TryDisconnectAsync(CancellationToken.None).ConfigureAwait(false);

        _readLoopCts?.Cancel();
        if (_readLoopTask != null)
        {
            try { await _readLoopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        TryKillAdapterProcess();
        _process.Dispose();
    }
}
