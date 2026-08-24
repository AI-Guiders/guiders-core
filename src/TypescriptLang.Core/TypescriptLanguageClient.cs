using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TypescriptLang;

/// <summary>Line-delimited JSON-RPC to a Node worker running typescript LanguageService.</summary>
public sealed class TypescriptLanguageClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly CancellationTokenSource _readCts = new();
    private readonly Task _readLoop;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private int _nextId;
    private bool _disposed;

    public string WorkerDir { get; }
    public bool IsAlive => !_disposed && !_process.HasExited;
    public string? LastError { get; private set; }

    private TypescriptLanguageClient(Process process, StreamWriter stdin, string workerDir)
    {
        _process = process;
        _stdin = stdin;
        WorkerDir = workerDir;
        _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public static async Task<TypescriptLanguageClient> StartAsync(
        string? workerDir = null,
        string? nodePath = null,
        CancellationToken cancellationToken = default)
    {
        workerDir ??= ResolveDefaultWorkerDir();
        var entry = Path.Combine(workerDir, "index.mjs");
        if (!File.Exists(entry))
            throw new FileNotFoundException($"TS worker entry not found: {entry}");

        var start = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(nodePath) ? "node" : nodePath,
            Arguments = Quote(entry),
            WorkingDirectory = workerDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Failed to start node worker at {entry}");

        // Drain stderr so the pipe never blocks; keep last line for diagnostics.
        var clientHolder = new TypescriptLanguageClient[1];
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.HasExited)
                {
                    var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line is null) break;
                    if (clientHolder[0] is { } c)
                        c.LastError = line;
                }
            }
            catch
            {
                // process exit
            }
        }, CancellationToken.None);

        var stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        var client = new TypescriptLanguageClient(process, stdin, workerDir);
        clientHolder[0] = client;

        // Warm ping
        await client.RequestAsync("ping", new { }, cancellationToken).ConfigureAwait(false);
        return client;
    }

    public Task<JsonElement> OpenProjectAsync(string projectRoot, string? tsconfigPath = null, CancellationToken ct = default) =>
        RequestAsync("open_project", new { projectRoot, tsconfigPath }, ct);

    public Task<JsonElement> GoToDefinitionAsync(string filePath, int line, int column, CancellationToken ct = default) =>
        RequestAsync("go_to_definition", new { filePath, line, column }, ct);

    public Task<JsonElement> FindUsagesAsync(string filePath, int line, int column, CancellationToken ct = default) =>
        RequestAsync("find_usages", new { filePath, line, column }, ct);

    public Task<JsonElement> GetDocumentSymbolsAsync(string filePath, CancellationToken ct = default) =>
        RequestAsync("get_document_symbols", new { filePath }, ct);

    public Task<JsonElement> GetSymbolAtPositionAsync(string filePath, int line, int column, CancellationToken ct = default) =>
        RequestAsync("get_symbol_at_position", new { filePath, line, column }, ct);

    public Task<JsonElement> GetDiagnosticsAsync(string filePath, CancellationToken ct = default) =>
        RequestAsync("get_diagnostics", new { filePath }, ct);

    public Task<JsonElement> ResolveProjectRootAsync(string path, CancellationToken ct = default) =>
        RequestAsync("resolve_project_root", new { path }, ct);

    public async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw new InvalidOperationException($"TS worker exited (code {_process.ExitCode}). stderr: {LastError}");

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var payload = JsonSerializer.Serialize(new { id, method, @params = parameters }, JsonOpts);
        await _stdin.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id))
                    continue;
                if (!_pending.TryGetValue(id, out var tcs))
                    continue;
                if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
                {
                    var err = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown error";
                    tcs.TrySetException(new InvalidOperationException($"TS worker {err}"));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    tcs.TrySetResult(result.Clone());
                }
                else
                {
                    tcs.TrySetResult(root.Clone());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            foreach (var kv in _pending)
                kv.Value.TrySetException(ex);
        }
    }

    public static string ResolveDefaultWorkerDir()
    {
        var env = Environment.GetEnvironmentVariable("CDP_TS_WORKER_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return Path.GetFullPath(env);

        var besideExe = Path.Combine(AppContext.BaseDirectory, "ts-worker");
        if (Directory.Exists(besideExe))
            return besideExe;

        // Dev: worker next to TypescriptLang.Core (guiders-core monorepo home).
        var probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "worker"));
        if (Directory.Exists(probe) && File.Exists(Path.Combine(probe, "index.mjs")))
            return probe;

        return besideExe;
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _readCts.Cancel();
        try
        {
            _stdin.Dispose();
        }
        catch
        {
            // ignore
        }

        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        _process.Dispose();
        _readCts.Dispose();
    }
}
