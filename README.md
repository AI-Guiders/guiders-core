# Guiders Core

Backend NuGet libraries shared by AI Guiders products (CDP habitat, Forge MCP hosts, Cascade tooling).

Sibling to **guiders-platform** (cross-product mechanics) — see [GUIDERS-ADR-0004](../guiders-platform/docs/adr/GUIDERS-ADR-0004-core-monorepo.md).

## Layout

```
src/          # ship-ready library packages (AIGuiders.*)
tests/        # unit tests per package
scripts/      # import / reference-fix tooling (migration)
```

## Packages

See [docs/packages-inventory.md](docs/packages-inventory.md).

## Build

```bash
dotnet build
dotnet test
```

Per-package `dotnet pack` — **Trusted Publishing** via [`.github/workflows/release.yml`](../.github/workflows/release.yml); см. [docs/nuget-publishing.md](docs/nuget-publishing.md).

## Local dev (cdp-mcp)

Clone `guiders-core` as sibling of `cdp-mcp`. `CdpMcp.csproj` resolves `$(GuidersCoreRoot)` when `src/Cdp.Core` exists (same pattern as `GuidersPlatformRoot`).

## ADR

- [GUIDERS-CORE-0001 — monorepo bootstrap](docs/adr/GUIDERS-CORE-0001-monorepo-bootstrap.md)

## Legacy repos

Individual `*-core` GitHub repos are **deprecated** in favor of this monorepo. Tags and NuGet package IDs are unchanged.
