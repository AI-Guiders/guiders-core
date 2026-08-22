#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cdp.Ignite.Client;

/// <summary>
/// Seat-scoped ignite arms file + habitat latch notify (terminal-mcp / lightweight hosts).
/// Full CDT fire stays in CDP; this publishes habitat SSOT for hooks and survivor seats.
/// </summary>
public static class IgniteArmStore
{
    public const string StoreSchema = "ignite_arms/v1";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    static readonly object Gate = new();
    static List<IgniteArm> Arms = [];
    static bool Loaded;

    /// <summary>Test hook: redirect store root and seat.</summary>
    public static string? RootOverrideForTests { get; set; }
    public static string? SeatOverrideForTests { get; set; }

    /// <summary>Disable arm/notify (unit tests).</summary>
    public static bool SuppressForTests { get; set; }

    public static string Seat =>
        SeatOverrideForTests
        ?? Environment.GetEnvironmentVariable("CDP_IGNITE_SEAT")?.Trim()
        ?? Environment.GetEnvironmentVariable("TERMINAL_IGNITE_SEAT")?.Trim()
        ?? "terminal";

    public static string StorePath
    {
        get
        {
            var root = RootOverrideForTests
                       ?? Path.Combine(
                           Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                           "cdp-mcp");
            var file = Seat switch
            {
                "cdp-debug" => "ignite-arms-cdp-debug.json",
                "cdp" => "ignite-arms-cdp.json",
                "terminal" => "ignite-arms-terminal.json",
                _ => $"ignite-arms-{Seat}.json"
            };
            return Path.Combine(root, file);
        }
    }

    public static IReadOnlyList<IgniteArm> Snapshot()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Select(Clone).ToList();
    }

    public static bool TryArm(
        string whenEvent,
        string armId,
        string task,
        bool once = true,
        bool okOnly = false,
        bool force = true)
    {
        if (SuppressForTests)
            return false;

        var ev = NormalizeEvent(whenEvent);
        var id = armId?.Trim() ?? "";
        if (id.Length == 0)
            return false;

        EnsureLoaded();
        lock (Gate)
        {
            if (force)
                Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

            Arms.Add(new IgniteArm
            {
                Id = id,
                Event = ev,
                Task = task,
                Reason = ev,
                Once = once,
                OkOnly = okOnly,
                Status = "armed",
                CreatedUtc = DateTimeOffset.UtcNow
            });
            PersistUnlocked();
        }

        return true;
    }

    /// <summary>Match armed event arms and publish habitat wake latch (non-blocking).</summary>
    public static int Notify(string eventName, bool ok, string? pulse = null, string? detail = null)
    {
        if (SuppressForTests)
            return 0;

        var ev = NormalizeEvent(eventName);
        List<IgniteArm> hits;
        EnsureLoaded();
        lock (Gate)
        {
            hits = Arms.Where(a =>
                    a.Status == "armed"
                    && a.Event.Equals(ev, StringComparison.OrdinalIgnoreCase)
                    && (!a.OkOnly || ok))
                .Select(Clone)
                .ToList();
        }

        var fired = 0;
        foreach (var arm in hits)
        {
            var charge = IgniteChargePolicy.ComposeMinimalWake(ok, pulse, detail);
            var doc = IgniteWakeLatch.Publish(
                arm.Id,
                charge,
                IgniteWakeLatch.ChannelHabitat,
                arm.Reason,
                arm.Task);
            if (doc is null)
                continue;

            lock (Gate)
            {
                var live = Arms.FirstOrDefault(a => a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase));
                if (live is null)
                    continue;
                live.Status = arm.Once ? "fired" : "armed";
                live.FiredUtc = DateTimeOffset.UtcNow;
                PersistUnlocked();
            }

            fired++;
        }

        return fired;
    }

    public static string NormalizeEvent(string? raw)
    {
        var e = (raw ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return e switch
        {
            "build" or "build_done" or "build_ok" or "build_finished" or "on_build" => "build_finished",
            "test" or "tests" or "test_done" or "test_finished" or "on_test" => "test_finished",
            "shell" or "shell_done" or "shell_finished" or "on_shell" => "shell_finished",
            "peer_ship" or "peer" or "leaf_done" or "leaf_ship" or "ship" or "shipped" => "peer_ship",
            _ when e.Length == 0 => "timer",
            _ => e
        };
    }

    static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (Loaded)
                return;
            Loaded = true;
            try
            {
                if (!File.Exists(StorePath))
                {
                    Arms = [];
                    return;
                }

                var raw = File.ReadAllText(StorePath);
                var doc = JsonSerializer.Deserialize<ArmStoreDoc>(raw, JsonOpts);
                Arms = doc?.Arms ?? [];
            }
            catch
            {
                Arms = [];
            }
        }
    }

    static void PersistUnlocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var doc = new ArmStoreDoc
            {
                Schema = StoreSchema,
                SavedUtc = DateTimeOffset.UtcNow,
                Arms = Arms
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = StorePath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, StorePath, overwrite: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    static IgniteArm Clone(IgniteArm a) => new()
    {
        Id = a.Id,
        Event = a.Event,
        Task = a.Task,
        Reason = a.Reason,
        Once = a.Once,
        OkOnly = a.OkOnly,
        Status = a.Status,
        CreatedUtc = a.CreatedUtc,
        FiredUtc = a.FiredUtc
    };
}
