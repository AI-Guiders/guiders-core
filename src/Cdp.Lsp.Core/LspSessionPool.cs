using System.Collections.Concurrent;

namespace Cdp.Lsp;

/// <summary>One LSP process per (language id + workspace root).</summary>
public sealed class LspSessionPool : IAsyncDisposable
{
    readonly ConcurrentDictionary<string, LspClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    IReadOnlyList<LspLaunchPreset> _presets = LspLaunchPreset.BuiltInDefaults;

    public void Configure(IReadOnlyList<LspLaunchPreset> presets) =>
        _presets = presets.Count > 0 ? presets : LspLaunchPreset.BuiltInDefaults;

    public IReadOnlyList<LspLaunchPreset> Presets => _presets;

    public bool TryGetPreset(string languageId, out LspLaunchPreset preset)
    {
        preset = _presets.FirstOrDefault(p =>
            p.Id.Equals(languageId, StringComparison.OrdinalIgnoreCase)
            || p.LanguageIds.Any(l => l.Equals(languageId, StringComparison.OrdinalIgnoreCase)))!;
        return preset is not null;
    }

    static string Key(string languageId, string root) =>
        languageId.Trim().ToLowerInvariant() + "|" + Path.GetFullPath(root);

    public async Task<LspClient> GetOrStartAsync(
        string languageId,
        string workspaceRoot,
        CancellationToken ct = default)
    {
        if (!TryGetPreset(languageId, out var preset))
            throw new ArgumentException($"No LSP preset for language '{languageId}'. Configure [[languages.lsp]].");

        var key = Key(languageId, workspaceRoot);
        lock (_gate)
        {
            if (_clients.TryGetValue(key, out var existing) && existing.IsAlive)
                return existing;
        }

        var client = await LspClient.StartAsync(preset, workspaceRoot, ct).ConfigureAwait(false);
        LspClient? stale = null;
        lock (_gate)
        {
            if (_clients.TryGetValue(key, out var raced) && raced.IsAlive)
            {
                stale = client;
                return raced;
            }

            if (_clients.TryGetValue(key, out var old))
                stale = old;
            _clients[key] = client;
        }

        if (stale is not null)
            await stale.DisposeAsync().ConfigureAwait(false);
        return client;
    }

    public async Task StopAllAsync()
    {
        List<LspClient> all;
        lock (_gate)
        {
            all = _clients.Values.ToList();
            _clients.Clear();
        }

        foreach (var c in all)
            await c.DisposeAsync().ConfigureAwait(false);
    }

    public object HealthSnapshot()
    {
        lock (_gate)
        {
            return _clients.Select(kv =>
            {
                var parts = kv.Key.Split('|', 2);
                return new
                {
                    language = parts[0],
                    root = parts.Length > 1 ? parts[1] : null,
                    warm = kv.Value.IsAlive,
                    pid = kv.Value.ProcessId,
                    resolved_command = kv.Value.ResolvedCommand,
                    caps = kv.Value.Caps,
                    last_error = kv.Value.LastError
                };
            }).ToArray();
        }
    }

    public ValueTask DisposeAsync() => new(StopAllAsync());
}
