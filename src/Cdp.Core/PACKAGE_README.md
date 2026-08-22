# AIGuiders.Cdp.Core

Shared **.NET 10** library for the Cognitive Dev Platform tool catalog:

`catalog = f(phase, object [, language])` with optional `intent` ranking; wire domains follow **Memory.*** / **dev** intuition (`memory_world`, `memory_task`, `debug`, `build`, …).

**License:** MIT  
**Source:** [github.com/AI-Guiders/cdp-core](https://github.com/AI-Guiders/cdp-core)

Prefer a sibling `ProjectReference` to `cdp-core` in the monorepo when developing; NuGet for standalone clones / CI.

## Public API (short)

| Type | Role |
|------|------|
| `CdpPhase` / `CdpObjectKind` / `CdpIntent` | Catalog axes |
| `LanguageRegistry` / `CdpLanguages` | Language ids from config (not a closed enum) |
| `CdpLayer` / `CdpDomains` | Memory/dev/ide layers + longest-prefix tool split |
| `ToolAffordance` / `SessionContext` / `PhaseObjectCatalog.Query` | Shortlist |
| `Wave1AffordanceSeed` | Seed table for CDP hosts |

Non-goal: free-text **goal** as a catalog key.

## Consumers

- `cdp-mcp` — stdio facade (meta `cdp_*` + shortlist)
- `CascadeIDE` — `ide_context` / `ide_tools` + filtered `ide_*` ListTools
