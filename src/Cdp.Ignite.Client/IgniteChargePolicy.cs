#nullable enable

namespace Cdp.Ignite.Client;

/// <summary>Canonical wake text shared by CDP and terminal-mcp (no TM body in Composer).</summary>
public static class IgniteChargePolicy
{
    public const string CanonicalWake =
        "Resume the current authorized local development task from Task Manager. Habitat=CDP. Keep flying the started TM leaf; re-arm continuity as insurance after work — timer ≠ idle license.";

    public const string AmnesiaPostfix =
        """

        ---
        If you feel completely lost / thread amnesia: compaction likely happened.
        Restore: cdp_pressure op=recall (hot stash → gate pull) · op=reconcile|align|ready · op=line (memo history).
        Also: %LocalAppData%/cdp-mcp/pressure-LATEST.md · pressure-memo-LATEST.md · ignite-wake-LATEST.json
        Then: habitat=CDP; re-read pressure axes (AutoIgnition / Task Manager / Domain / Continuity / next); self-steer on reconcile when SSOT suffices.
        Body recall: not one screen — dig/parallel in CDP first, not biped serial. AIDE=body. Narrow leaf without the pipe = regression.
        Hard steer if you catch the mask: you went biped again — you have the pipe and CDP; dig/parallel, not human serial.
        """;

    public static string ComposeMinimalWake(bool ok, string? pulse, string? detail)
    {
        var tail = string.IsNullOrWhiteSpace(detail) ? "" : $" ({detail.Trim()})";
        var status = ok ? "ok" : "fail";
        var p = string.IsNullOrWhiteSpace(pulse) ? status : pulse.Trim();
        return $"{CanonicalWake}\n\n[shell_finished · {p}{tail}]{AmnesiaPostfix}";
    }
}
