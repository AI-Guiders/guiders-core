# GitMcp.Core

Shared construction of `git` CLI argument lists for **git-mcp** and **Cascade IDE** (ADR 0019 in cascade-ide).

## Source of truth

Monorepo **[AI-Guiders/guiders-core](https://github.com/AI-Guiders/guiders-core)** — `src/GitMcp.Core/`.

Legacy standalone repo `git-mcp-core` is deprecated.

## Layout

- Target: **.NET 10**, **C# 14**.
- No dependency on MCP SDK or Avalonia — only argv shapes and validation messages (`GitArgsResult`).

## Consumers

- **NuGet:** [`AIGuiders.GitMcp.Core`](https://www.nuget.org/packages/AIGuiders.GitMcp.Core)
- **Sibling dev:** `ProjectReference` via `GuidersCoreRoot` (cdp-mcp) or `src/GitMcp.Core/GitMcp.Core.csproj`

## Publish

Trusted Publishing: `AI-Guiders/guiders-core` + `release.yml` — см. [docs/nuget-publishing.md](../../../docs/nuget-publishing.md).

## Build

```bash
dotnet build src/GitMcp.Core/GitMcp.Core.csproj
```

Tests for `GitCommandBuilder` live in the **git-mcp** repository (`GitMcp.Tests`).
