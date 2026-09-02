# GUIDERS-CORE-0002: Package intelligence (L1)

**Status:** accepted (2026-08-23)

## Context

CDP habitat exposes L0 package mutate via `cdp_pkg_*` / `PackageOps` (dotnet/npm shell). Microsoft's `NuGet.Mcp.Server` adds CVE audit, upgrade planning, and supply-chain review — but ships only as a **dotnet tool** (no consumable library API; `NuGet.Solver.Core` is bundled, not on nuget.org).

Operators want the same intelligence **in-proc** inside CDP/Citizen without a subprocess bridge.

## Decision

Three layers:

| Layer | Owner | Scope |
|-------|-------|-------|
| **L0 mutate** | `Cdp.ScriptableIde` / `PackageOps` | list, find, add, remove, update, outdated |
| **L1 intelligence** | **`Cdp.PackageIntelligence`** (new) | audit, latest metadata, upgrade plan, supply-chain review |
| **L2 offline** | `agent-nuget-pm` (ANPM) | air-gap feeds — unchanged |

### L1 implementation

1. New library **`AIGuiders.Cdp.PackageIntelligence`** under `guiders-core/src/Cdp.PackageIntelligence/`.
2. Use **NuGet client libraries** (`NuGet.Protocol`, `NuGet.Configuration`, `NuGet.Versioning`) — same MIT stack as NuGet/Home, not a `<PackageReference>` to `NuGet.Mcp.Server`.
3. Vulnerability discovery: `dotnet list package --vulnerable --format json` (SDK 10) + structured parse.
4. Upgrade planning: audit + optional feed hints; **apply** via SDK `dotnet package update --vulnerable` (`cdp_pkg_fix_vuln`). Microsoft `NuGet.Solver.Core` is not a consumable package — do not wait for it.
5. New habitat tools: `cdp_pkg_audit`, `cdp_pkg_latest`, `cdp_pkg_upgrade_plan`, `cdp_pkg_fix_vuln`, `cdp_pkg_supply_chain`.
6. Cursor `user-nuget` MCP stays complementary for IDE agents; habitat gets in-proc parity.

### Boundaries

- **ANPM** remains L2 for private/offline feeds; L1 reads live nuget.org (or session `NuGet.Config` sources).
- L0 does not grow CVE logic — callers use L1 tools first, then L0 `cdp_pkg_update` / `dotnet package update --vulnerable`.

## Consequences

- `Cdp.ScriptableIde` references `Cdp.PackageIntelligence` and exposes `PackageIntelligenceOps`.
- `cdp-mcp` wires new Meta tools + Citizen `@intent pkg audit|latest|upgrade_plan|supply_chain`.
- Future: if Microsoft publishes `NuGet.Solver.Core` on nuget.org, evaluate for richer graph planning; SDK path remains default apply.

## Attribution

Design informed by [NuGet MCP Server](https://github.com/NuGet/Home) (MIT). Implementation is original in-proc code using public NuGet client APIs.
