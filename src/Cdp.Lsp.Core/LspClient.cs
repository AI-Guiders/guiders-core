using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cdp.Lsp;

/// <summary>Stdio LSP client (Content-Length framing) for CDP commodity IDE verbs.</summary>
public sealed class LspClient : IAsyncDisposable
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    readonly Process _process;
    readonly Stream _stdin;
    readonly CancellationTokenSource _cts = new();
    readonly Task _readLoop;
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    readonly ConcurrentDictionary<string, JsonArray> _publishedDiagsRaw = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, int> _docVersions = new(StringComparer.OrdinalIgnoreCase);
    readonly object _writeGate = new();
    readonly List<JsonNode> _pendingCodeActions = [];
    int _nextId;
    bool _disposed;

    public LspLaunchPreset Preset { get; }
    public string WorkspaceRoot { get; }
    /// <summary>Resolved executable display (after PATH / npm-shim rewrite).</summary>
    public string ResolvedCommand { get; }
    public bool IsAlive => !_disposed && !_process.HasExited;
    public int? ProcessId => _disposed || _process.HasExited ? null : _process.Id;
    public string? LastError { get; private set; }
    public LspServerCaps Caps { get; private set; } = new();

    LspClient(Process process, Stream stdin, LspLaunchPreset preset, string workspaceRoot, string resolvedCommand)
    {
        _process = process;
        _stdin = stdin;
        Preset = preset;
        WorkspaceRoot = workspaceRoot;
        ResolvedCommand = resolvedCommand;
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public static async Task<LspClient> StartAsync(
        LspLaunchPreset preset,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preset.Command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        workspaceRoot = Path.GetFullPath(workspaceRoot);

        var launch = LspCommandResolver.Resolve(preset);
        var psi = new ProcessStartInfo
        {
            FileName = launch.FileName,
            WorkingDirectory = workspaceRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in launch.Args)
            psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start LSP: {launch.Display}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"lsp_server_missing: '{launch.Display}' (language={preset.Id}). Install basedpyright or pyright-langserver, or fix [languages.lsp] command. {ex.Message}",
                ex);
        }

        var holder = new LspClient[1];
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.HasExited)
                {
                    var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line is null) break;
                    if (holder[0] is { } c)
                        c.LastError = line;
                }
            }
            catch { /* exit */ }
        }, CancellationToken.None);

        var client = new LspClient(process, process.StandardInput.BaseStream, preset, workspaceRoot, launch.Display);
        holder[0] = client;
        await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

    async Task InitializeAsync(CancellationToken ct)
    {
        var rootUri = PathToUri(WorkspaceRoot);
        var result = await RequestAsync("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = rootUri,
            ["rootPath"] = WorkspaceRoot,
            ["capabilities"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["synchronization"] = new JsonObject
                    {
                        ["didSave"] = true,
                        ["dynamicRegistration"] = false
                    },
                    ["definition"] = new JsonObject { ["linkSupport"] = true },
                    ["references"] = new JsonObject(),
                    ["documentSymbol"] = new JsonObject
                    {
                        ["hierarchicalDocumentSymbolSupport"] = true
                    },
                    ["hover"] = new JsonObject
                    {
                        ["contentFormat"] = new JsonArray("markdown", "plaintext")
                    },
                    ["completion"] = new JsonObject
                    {
                        ["completionItem"] = new JsonObject { ["snippetSupport"] = true }
                    },
                    ["signatureHelp"] = new JsonObject
                    {
                        ["signatureInformation"] = new JsonObject
                        {
                            ["parameterInformation"] = new JsonObject { ["labelOffsetSupport"] = true }
                        }
                    },
                    ["rename"] = new JsonObject { ["prepareSupport"] = true },
                    ["codeAction"] = new JsonObject
                    {
                        ["codeActionLiteralSupport"] = new JsonObject
                        {
                            ["codeActionKind"] = new JsonObject
                            {
                                ["valueSet"] = new JsonArray(
                                    "", "quickfix", "refactor", "refactor.extract",
                                    "refactor.inline", "refactor.rewrite", "source")
                            }
                        },
                        ["resolveSupport"] = new JsonObject
                        {
                            ["properties"] = new JsonArray("edit")
                        }
                    },
                    ["diagnostic"] = new JsonObject
                    {
                        ["dynamicRegistration"] = false
                    },
                    ["publishDiagnostics"] = new JsonObject()
                },
                ["workspace"] = new JsonObject
                {
                    ["workspaceEdit"] = new JsonObject
                    {
                        ["documentChanges"] = true
                    },
                    ["applyEdit"] = true
                }
            },
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "cdp-lsp",
                ["version"] = "0.1.0"
            },
            ["workspaceFolders"] = new JsonArray(
                new JsonObject { ["uri"] = rootUri, ["name"] = Path.GetFileName(WorkspaceRoot.TrimEnd('\\', '/')) })
        }, ct).ConfigureAwait(false);

        Caps = ParseCaps(result);
        await NotifyAsync("initialized", new JsonObject()).ConfigureAwait(false);
    }

    static LspServerCaps ParseCaps(JsonNode? result)
    {
        var caps = result?["capabilities"];
        if (caps is null)
            return new LspServerCaps();
        return new LspServerCaps
        {
            Definition = caps["definitionProvider"] is not null,
            References = caps["referencesProvider"] is not null,
            DocumentSymbol = caps["documentSymbolProvider"] is not null,
            Hover = caps["hoverProvider"] is not null,
            Rename = caps["renameProvider"] is not null,
            CodeAction = caps["codeActionProvider"] is not null,
            DiagnosticPull = caps["diagnosticProvider"] is not null
        };
    }

    public async Task DidOpenAsync(string absolutePath, string text, string languageId, CancellationToken ct = default)
    {
        var uri = PathToUri(absolutePath);
        InvalidatePublishedDiags(absolutePath);
        var ver = _docVersions.AddOrUpdate(uri, 1, (_, v) => v + 1);
        await NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = languageId,
                ["version"] = ver,
                ["text"] = text
            }
        }).ConfigureAwait(false);
        _ = ct;
    }

    public async Task DidChangeAsync(string absolutePath, string text, CancellationToken ct = default)
    {
        var uri = PathToUri(absolutePath);
        InvalidatePublishedDiags(absolutePath);
        var ver = _docVersions.AddOrUpdate(uri, 1, (_, v) => v + 1);
        await NotifyAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["version"] = ver
            },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text })
        }).ConfigureAwait(false);
        _ = ct;
    }

    public async Task EnsureOpenAsync(string absolutePath, string? text, string languageId, CancellationToken ct = default)
    {
        var uri = PathToUri(absolutePath);
        var body = text ?? await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false);
        if (_docVersions.ContainsKey(uri))
            await DidChangeAsync(absolutePath, body, ct).ConfigureAwait(false);
        else
            await DidOpenAsync(absolutePath, body, languageId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LspLocation>> GoToDefinitionAsync(
        string path, int line1, int col1, CancellationToken ct = default)
    {
        var result = await RequestAsync("textDocument/definition", TextDocPosition(path, line1, col1), ct)
            .ConfigureAwait(false);
        return ParseLocations(result);
    }

    public async Task<IReadOnlyList<LspLocation>> FindReferencesAsync(
        string path, int line1, int col1, CancellationToken ct = default)
    {
        var p = TextDocPosition(path, line1, col1);
        p["context"] = new JsonObject { ["includeDeclaration"] = true };
        var result = await RequestAsync("textDocument/references", p, ct).ConfigureAwait(false);
        return ParseLocations(result);
    }

    public async Task<IReadOnlyList<LspDocumentSymbol>> DocumentSymbolsAsync(
        string path, CancellationToken ct = default)
    {
        var result = await RequestAsync("textDocument/documentSymbol", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = PathToUri(path) }
        }, ct).ConfigureAwait(false);
        return ParseDocumentSymbols(result);
    }

    public async Task<LspHoverInfo?> HoverAsync(string path, int line1, int col1, CancellationToken ct = default)
    {
        var result = await RequestAsync("textDocument/hover", TextDocPosition(path, line1, col1), ct)
            .ConfigureAwait(false);
        if (result is null || result.GetValueKind() == JsonValueKind.Null)
            return null;
        var contents = FormatHoverContents(result["contents"]);
        LspRange? range = null;
        if (result["range"] is JsonObject r)
            range = ParseRange(r);
        return new LspHoverInfo(contents, range);
    }

    public async Task<JsonNode?> CompletionAsync(string path, int line1, int col1, CancellationToken ct = default) =>
        await RequestAsync("textDocument/completion", TextDocPosition(path, line1, col1), ct).ConfigureAwait(false);

    public async Task<JsonNode?> SignatureHelpAsync(string path, int line1, int col1, CancellationToken ct = default) =>
        await RequestAsync("textDocument/signatureHelp", TextDocPosition(path, line1, col1), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<LspDiagnostic>> DiagnosticsAsync(string path, CancellationToken ct = default)
    {
        var pathKey = PathKey(path);
        if (Caps.DiagnosticPull)
        {
            try
            {
                var result = await RequestAsync("textDocument/diagnostic", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = PathToUri(path) }
                }, ct).ConfigureAwait(false);
                if (result?["items"] is JsonArray items)
                    return ParseDiagnosticArray(items);
                if (result?["kind"]?.GetValue<string>() == "full" && result["items"] is JsonArray full)
                    return ParseDiagnosticArray(full);
            }
            catch
            {
                // fall through to publish cache
            }
        }

        // Pyright pushes publishDiagnostics async; URI form may differ (d%3A vs D:/) — key by full path.
        for (var i = 0; i < 40; i++)
        {
            if (_publishedDiagsRaw.TryGetValue(pathKey, out var raw))
                return ParseDiagnosticArray(raw);
            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        return [];
    }

    public async Task<LspWorkspaceEdit?> RenameAsync(
        string path, int line1, int col1, string newName, CancellationToken ct = default)
    {
        try
        {
            await RequestAsync("textDocument/prepareRename", TextDocPosition(path, line1, col1), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // prepare optional
        }

        var p = TextDocPosition(path, line1, col1);
        p["newName"] = newName;
        var result = await RequestAsync("textDocument/rename", p, ct).ConfigureAwait(false);
        return ParseWorkspaceEdit(result);
    }

    public async Task<IReadOnlyList<LspCodeActionItem>> CodeActionsAsync(
        string path, int line1, int col1, CancellationToken ct = default)
    {
        // Wait for publish; pass RAW diagnostics (incl. data) — pyright quickfix need them.
        _ = await DiagnosticsAsync(path, ct).ConfigureAwait(false);
        var pos = ToZeroBased(line1, col1);
        var diagJson = new JsonArray();
        LspRange? cover = null;
        if (_publishedDiagsRaw.TryGetValue(PathKey(path), out var published))
        {
            foreach (var node in published)
            {
                if (node is null) continue;
                diagJson.Add(node.DeepClone());
                if (node is JsonObject dobj && dobj["range"] is JsonObject rr)
                {
                    var r = ParseRange(rr);
                    if (pos.Line < r.Start.Line || pos.Line > r.End.Line) continue;
                    if (pos.Line == r.Start.Line && pos.Character < r.Start.Character) continue;
                    if (pos.Line == r.End.Line && pos.Character > r.End.Character) continue;
                    cover = r;
                }
            }
        }

        var range = cover is { } c
            ? new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = c.Start.Line, ["character"] = c.Start.Character },
                ["end"] = new JsonObject { ["line"] = c.End.Line, ["character"] = c.End.Character }
            }
            : new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = pos.Line, ["character"] = pos.Character },
                ["end"] = new JsonObject { ["line"] = pos.Line, ["character"] = pos.Character }
            };

        var result = await RequestAsync("textDocument/codeAction", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = PathToUri(path) },
            ["range"] = range,
            ["context"] = new JsonObject { ["diagnostics"] = diagJson }
        }, ct).ConfigureAwait(false);

        _pendingCodeActions.Clear();
        var list = new List<LspCodeActionItem>();
        if (result is not JsonArray arr)
            return list;

        var idx = 0;
        foreach (var item in arr)
        {
            if (item is null) continue;
            // Command-only entries skip
            if (item is JsonObject obj)
            {
                var title = obj["title"]?.GetValue<string>()
                    ?? obj["command"]?["title"]?.GetValue<string>()
                    ?? $"action_{idx}";
                var kind = obj["kind"]?.GetValue<string>();
                var hasEdit = obj["edit"] is not null;
                var needsResolve = !hasEdit && obj["data"] is not null;
                _pendingCodeActions.Add(item.DeepClone());
                list.Add(new LspCodeActionItem(idx, title, kind, hasEdit, needsResolve));
                idx++;
            }
            else if (item is JsonValue)
            {
                // some servers return Command
                _pendingCodeActions.Add(item.DeepClone());
                list.Add(new LspCodeActionItem(idx, item.ToJsonString(), null, false, false));
                idx++;
            }
        }

        return list;
    }

    public async Task<LspWorkspaceEdit?> ApplyCodeActionAsync(int index, CancellationToken ct = default)
    {
        if (index < 0 || index >= _pendingCodeActions.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "code action index out of range; call CodeActionsAsync first.");

        var node = _pendingCodeActions[index];
        if (node is JsonObject obj)
        {
            if (obj["edit"] is not null)
                return ParseWorkspaceEdit(obj["edit"]);

            if (obj["data"] is not null)
            {
                var resolved = await RequestAsync("codeAction/resolve", obj, ct).ConfigureAwait(false);
                if (resolved?["edit"] is not null)
                    return ParseWorkspaceEdit(resolved["edit"]);
            }

            if (obj["command"] is not null)
                throw new InvalidOperationException(
                    "code_action_command_only: server returned a command without workspace edit; not supported in CDP v0.");
        }

        throw new InvalidOperationException("code_action_no_edit");
    }

    JsonObject TextDocPosition(string path, int line1, int col1)
    {
        var pos = ToZeroBased(line1, col1);
        return new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = PathToUri(path) },
            ["position"] = new JsonObject { ["line"] = pos.Line, ["character"] = pos.Character }
        };
    }

    static LspPosition ToZeroBased(int line1, int col1) =>
        new(Math.Max(0, line1 - 1), Math.Max(0, col1 - 1));

    public static (int Line, int Column) ToOneBased(LspPosition p) =>
        (p.Line + 1, p.Character + 1);

    public static string PathToUri(string path)
    {
        var full = Path.GetFullPath(path);
        return new Uri(full).AbsoluteUri;
    }

    public static string UriToPath(string uri)
    {
        var u = new Uri(uri);
        var local = u.LocalPath;
        // file:///d%3A/foo → LocalPath "/d:/foo" on some runtimes; GetFullPath then ≠ "D:\foo".
        if (OperatingSystem.IsWindows()
            && local.Length >= 3
            && local[0] == '/'
            && char.IsAsciiLetter(local[1])
            && local[2] == ':')
        {
            local = local[1..];
        }

        return Path.GetFullPath(local);
    }

    /// <summary>
    /// Canonical disk path key. Pyright may publish <c>file:///d%3A/...</c> while client opens
    /// <c>file:///D:/...</c> — string URI keys miss; full path matches.
    /// </summary>
    public static string PathKey(string uriOrPath)
    {
        if (uriOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(UriToPath(uriOrPath));
        return Path.GetFullPath(uriOrPath);
    }

    void InvalidatePublishedDiags(string absolutePath) =>
        _publishedDiagsRaw.TryRemove(PathKey(absolutePath), out _);

    static IReadOnlyList<LspLocation> ParseLocations(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
            return [];
        if (node is JsonObject single)
            return ParseLocation(single) is { } loc ? [loc] : [];
        if (node is JsonArray arr)
        {
            var list = new List<LspLocation>();
            foreach (var el in arr)
            {
                if (el is JsonObject o && ParseLocation(o) is { } loc)
                    list.Add(loc);
            }

            return list;
        }

        return [];
    }

    static LspLocation? ParseLocation(JsonObject o)
    {
        // Location or LocationLink
        var targetUri = o["uri"]?.GetValue<string>() ?? o["targetUri"]?.GetValue<string>();
        var rangeNode = o["range"] as JsonObject
            ?? o["targetSelectionRange"] as JsonObject
            ?? o["targetRange"] as JsonObject;
        if (targetUri is null || rangeNode is null)
            return null;
        return new LspLocation(targetUri, ParseRange(rangeNode));
    }

    static LspRange ParseRange(JsonObject r)
    {
        var s = (JsonObject)r["start"]!;
        var e = (JsonObject)r["end"]!;
        return new LspRange(
            new LspPosition(s["line"]!.GetValue<int>(), s["character"]!.GetValue<int>()),
            new LspPosition(e["line"]!.GetValue<int>(), e["character"]!.GetValue<int>()));
    }

    static IReadOnlyList<LspDocumentSymbol> ParseDocumentSymbols(JsonNode? node)
    {
        if (node is not JsonArray arr)
            return [];
        var list = new List<LspDocumentSymbol>();
        foreach (var el in arr)
        {
            if (el is not JsonObject o) continue;
            // DocumentSymbol vs SymbolInformation
            if (o["location"] is JsonObject loc)
            {
                var range = ParseRange((JsonObject)loc["range"]!);
                list.Add(new LspDocumentSymbol(
                    o["name"]?.GetValue<string>() ?? "?",
                    SymbolKindName(o["kind"]?.GetValue<int>() ?? 0),
                    range,
                    range,
                    null));
            }
            else if (o["range"] is JsonObject rangeObj)
            {
                var range = ParseRange(rangeObj);
                var sel = o["selectionRange"] is JsonObject sr ? ParseRange(sr) : range;
                IReadOnlyList<LspDocumentSymbol>? children = null;
                if (o["children"] is JsonArray ch)
                    children = ParseDocumentSymbols(ch);
                list.Add(new LspDocumentSymbol(
                    o["name"]?.GetValue<string>() ?? "?",
                    SymbolKindName(o["kind"]?.GetValue<int>() ?? 0),
                    range,
                    sel,
                    children));
            }
        }

        return list;
    }

    static string SymbolKindName(int kind) => kind switch
    {
        5 => "class",
        6 => "method",
        7 => "property",
        8 => "field",
        12 => "function",
        13 => "variable",
        14 => "constant",
        2 => "module",
        10 => "enum",
        11 => "interface",
        _ => $"kind_{kind}"
    };

    static string? FormatHoverContents(JsonNode? contents)
    {
        if (contents is null) return null;
        if (contents is JsonValue v && v.TryGetValue<string>(out var s))
            return s;
        if (contents is JsonObject o)
        {
            if (o["value"]?.GetValue<string>() is { } mv)
                return mv;
            if (o["language"] is not null && o["value"] is not null)
                return o["value"]!.GetValue<string>();
        }

        if (contents is JsonArray arr)
        {
            var parts = new List<string>();
            foreach (var el in arr)
            {
                var p = FormatHoverContents(el);
                if (!string.IsNullOrWhiteSpace(p))
                    parts.Add(p!);
            }

            return string.Join("\n", parts);
        }

        return contents.ToJsonString();
    }

    static IReadOnlyList<LspDiagnostic> ParseDiagnosticArray(JsonArray items)
    {
        var list = new List<LspDiagnostic>();
        foreach (var el in items)
        {
            if (el is not JsonObject o || o["range"] is not JsonObject rr) continue;
            var sev = o["severity"]?.GetValue<int>() switch
            {
                1 => "error",
                2 => "warning",
                3 => "information",
                4 => "hint",
                _ => "unknown"
            };
            string? code = null;
            if (o["code"] is JsonValue cv)
                code = cv.ToString();
            else if (o["code"] is JsonObject co)
                code = co["value"]?.ToString();
            list.Add(new LspDiagnostic(
                ParseRange(rr),
                sev,
                code,
                o["message"]?.GetValue<string>() ?? "",
                o["source"]?.GetValue<string>()));
        }

        return list;
    }

    public static LspWorkspaceEdit? ParseWorkspaceEdit(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;
        var changes = new Dictionary<string, IReadOnlyList<LspTextEdit>>(StringComparer.OrdinalIgnoreCase);

        if (obj["changes"] is JsonObject ch)
        {
            foreach (var (uri, editsNode) in ch)
            {
                if (editsNode is not JsonArray editsArr) continue;
                var edits = new List<LspTextEdit>();
                foreach (var e in editsArr)
                {
                    if (e is JsonObject eo && eo["range"] is JsonObject rr)
                        edits.Add(new LspTextEdit(ParseRange(rr), eo["newText"]?.GetValue<string>() ?? ""));
                }

                changes[uri] = edits;
            }
        }

        if (obj["documentChanges"] is JsonArray dc)
        {
            foreach (var item in dc)
            {
                if (item is not JsonObject dco) continue;
                var uri = dco["textDocument"]?["uri"]?.GetValue<string>();
                if (uri is null || dco["edits"] is not JsonArray editsArr) continue;
                var edits = new List<LspTextEdit>();
                foreach (var e in editsArr)
                {
                    if (e is JsonObject eo && eo["range"] is JsonObject rr)
                        edits.Add(new LspTextEdit(ParseRange(rr), eo["newText"]?.GetValue<string>() ?? ""));
                }

                if (changes.TryGetValue(uri, out var existing))
                    changes[uri] = existing.Concat(edits).ToArray();
                else
                    changes[uri] = edits;
            }
        }

        return changes.Count == 0 ? null : new LspWorkspaceEdit(changes);
    }

    async Task NotifyAsync(string method, JsonObject parameters)
    {
        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters
        };
        await WriteMessageAsync(msg.ToJsonString(JsonOpts)).ConfigureAwait(false);
    }

    async Task<JsonNode?> RequestAsync(string method, JsonObject parameters, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw new InvalidOperationException($"LSP exited (code {_process.ExitCode}). stderr: {LastError}");

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };
        await WriteMessageAsync(msg.ToJsonString(JsonOpts)).ConfigureAwait(false);
        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    async Task WriteMessageAsync(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeGate)
        {
            _stdin.Write(header, 0, header.Length);
            _stdin.Write(body, 0, body.Length);
            _stdin.Flush();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    async Task ReadLoopAsync(CancellationToken ct)
    {
        var stdout = _process.StandardOutput.BaseStream;
        var headerBuf = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && !_process.HasExited)
            {
                headerBuf.SetLength(0);
                // read until \r\n\r\n
                var match = 0;
                while (match < 4)
                {
                    var b = stdout.ReadByte();
                    if (b < 0) return;
                    headerBuf.WriteByte((byte)b);
                    match = (match, b) switch
                    {
                        (0, '\r') => 1,
                        (1, '\n') => 2,
                        (2, '\r') => 3,
                        (3, '\n') => 4,
                        (_, '\r') => 1,
                        _ => 0
                    };
                }

                var headerText = Encoding.ASCII.GetString(headerBuf.ToArray());
                var len = 0;
                foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        len = int.Parse(line["Content-Length:".Length..].Trim());
                }

                if (len <= 0) continue;
                var body = new byte[len];
                var read = 0;
                while (read < len)
                {
                    var n = await stdout.ReadAsync(body.AsMemory(read, len - read), ct).ConfigureAwait(false);
                    if (n == 0) return;
                    read += n;
                }

                var json = Encoding.UTF8.GetString(body);
                HandleMessage(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LastError = ex.Message;
            foreach (var kv in _pending)
                kv.Value.TrySetException(ex);
        }
    }

    void HandleMessage(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch
        {
            return;
        }

        if (root is null) return;

        // Response
        if (root["id"] is not null && root["method"] is null)
        {
            var idNode = root["id"];
            var id = idNode?.GetValueKind() == JsonValueKind.Number
                ? idNode.GetValue<int>()
                : int.TryParse(idNode?.ToString(), out var parsed) ? parsed : -1;
            if (id >= 0 && _pending.TryRemove(id, out var tcs))
            {
                if (root["error"] is JsonNode err)
                {
                    var msg = err["message"]?.GetValue<string>() ?? err.ToJsonString();
                    tcs.TrySetException(new InvalidOperationException($"LSP error: {msg}"));
                }
                else
                {
                    tcs.TrySetResult(root["result"]?.DeepClone());
                }
            }

            return;
        }

        // Notification / server request
        var method = root["method"]?.GetValue<string>();
        if (method == "textDocument/publishDiagnostics" && root["params"] is JsonObject p)
        {
            var uri = p["uri"]?.GetValue<string>();
            if (uri is not null && p["diagnostics"] is JsonArray diags)
                _publishedDiagsRaw[PathKey(uri)] = (JsonArray)diags.DeepClone()!;
            return;
        }

        // Server requests we don't handle — reply empty error if has id
        if (root["id"] is not null && method is not null)
        {
            var id = root["id"]!.GetValueKind() == JsonValueKind.Number
                ? root["id"]!.GetValue<int>()
                : -1;
            if (id >= 0)
            {
                var reply = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32601,
                        ["message"] = $"Method not supported by CDP LSP client: {method}"
                    }
                };
                _ = WriteMessageAsync(reply.ToJsonString(JsonOpts));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await NotifyAsync("exit", new JsonObject()).ConfigureAwait(false);
        }
        catch { /* ignore */ }

        _cts.Cancel();
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { /* ignore */ }

        try { await _readLoop.ConfigureAwait(false); } catch { /* ignore */ }
        _cts.Dispose();
        _process.Dispose();
    }
}
