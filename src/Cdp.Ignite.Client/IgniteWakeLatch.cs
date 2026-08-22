#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cdp.Ignite.Client;

/// <summary>Habitat/composer wake latch — %LocalAppData%/cdp-mcp/ignite-wake-LATEST.json</summary>
public static class IgniteWakeLatch
{
    public const string Schema = "ignite_wake_latch/v0";
    public const string ChannelComposer = "composer";
    public const string ChannelHabitat = "habitat";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Test hook: redirect latch root.</summary>
    public static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "ignite-wake-LATEST.json");

    public static WakeDoc? Publish(
        string armId,
        string charge,
        string channel,
        string? reason = null,
        string? task = null)
    {
        var id = armId?.Trim() ?? "";
        var body = charge?.Trim() ?? "";
        var ch = NormalizeChannel(channel);
        if (id.Length == 0 || body.Length == 0 || ch is null)
            return null;

        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new WakeDoc
            {
                Schema = Schema,
                ArmId = id,
                Channel = ch,
                Charge = body,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                Task = string.IsNullOrWhiteSpace(task) ? null : task.Trim(),
                StampedUtc = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public static WakeDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<WakeDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public static string? NormalizeChannel(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Trim().ToLowerInvariant() switch
            {
                "composer" or "cdt" or "cursor" => ChannelComposer,
                "habitat" or "intercom" or "duplex" => ChannelHabitat,
                _ => null
            };

    public sealed class WakeDoc
    {
        public string Schema { get; set; } = IgniteWakeLatch.Schema;
        public string ArmId { get; set; } = "";
        public string Channel { get; set; } = ChannelComposer;
        public string Charge { get; set; } = "";
        public string? Reason { get; set; }
        public string? Task { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
    }
}
