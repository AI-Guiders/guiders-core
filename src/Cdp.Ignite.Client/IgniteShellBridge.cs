#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cdp.Ignite.Client;

/// <summary>Background terminal shell ↔ AutoIgnition (terminal-mcp seat).</summary>
public static class IgniteShellBridge
{
    public const string BackgroundArmIdPrefix = "terminal-shell-bg-";

    public static void OnShellFinished(
        string tab,
        string command,
        int exitCode,
        bool background)
    {
        if (!background)
            return;

        IgniteArmStore.Notify(
            "shell_finished",
            ok: exitCode == 0,
            pulse: tab,
            detail: Truncate(command, 120));
    }

    public static bool TryAutoArmBackground(
        string? tab,
        string? command,
        bool enabled,
        out string? armId)
    {
        armId = null;
        if (!enabled || IgniteArmStore.SuppressForTests)
            return false;
        if (string.Equals(Environment.GetEnvironmentVariable("TERMINAL_SHELL_IGNITE_ARM"), "0", StringComparison.Ordinal))
            return false;

        var safeTab = string.IsNullOrWhiteSpace(tab) ? "main" : tab.Trim();
        armId = BackgroundArmIdPrefix + safeTab.ToLowerInvariant();
        var task = BuildTaskLabel(safeTab, command);
        return IgniteArmStore.TryArm(
            "shell_finished",
            armId,
            task,
            once: true,
            okOnly: false,
            force: true);
    }

    public static bool ResolveIgniteArmEnabled(bool background, bool? igniteArmArg)
    {
        if (!background)
            return false;
        return igniteArmArg ?? true;
    }

    public static string AnnotateBackgroundRun(string json, string? armId, JsonSerializerOptions? pretty = null)
    {
        if (string.IsNullOrWhiteSpace(armId))
            return json;
        pretty ??= new JsonSerializerOptions { WriteIndented = true };
        try
        {
            var node = JsonNode.Parse(json)?.AsObject();
            if (node is null)
                return json;
            node["ignite"] = new JsonObject
            {
                ["armed"] = true,
                ["when"] = "shell_finished",
                ["arm_id"] = armId,
                ["seat"] = IgniteArmStore.Seat,
                ["hint"] = "Habitat wake latch on tab exit (terminal_* background). Poll terminal_last."
            };
            return node.ToJsonString(pretty);
        }
        catch
        {
            return json;
        }
    }

    static string BuildTaskLabel(string tab, string? command)
    {
        var cmd = Truncate((command ?? "").Trim(), 96);
        if (cmd.Length == 0)
            return $"terminal:{tab}";
        return tab.Equals("main", StringComparison.OrdinalIgnoreCase)
            ? $"terminal: {cmd}"
            : $"terminal:{tab}: {cmd}";
    }

    static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
