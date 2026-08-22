# GUIDERS-CORE-0001: Monorepo bootstrap

**Status:** accepted (2026-08-22)

## Context

AI Guiders shipped ~18 backend libraries as separate `*-core` GitHub repos. Cross-package refactors required N commits; CI/release duplicated; local dev needed many sibling checkouts.

## Decision

1. **`guiders-core` monorepo** — all `AIGuiders.*` backend libraries under `src/`, tests under `tests/`.
2. **Package IDs unchanged** — NuGet consumers keep existing names/versions.
3. **Import scripts** — `scripts/import-core-packages.ps1` + `fix-references.ps1` for one-shot migration from legacy repos.
4. **Products** (`cdp-mcp`, etc.) resolve via `GuidersCoreRoot` sibling `ProjectReference` or NuGet (`AidUseNuGet`).

## Consequences

- Legacy repos get deprecation README pointing here.
- Per-package Trusted Publishing workflows added incrementally.
- Smoke/tool projects from old repos are **not** migrated (library surface only).
