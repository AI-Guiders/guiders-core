# GitMcp.Core

Shared construction of `git` CLI argument lists for **git-mcp** and **Cascade IDE** (ADR 0019 in cascade-ide).

## Remotes (политика)

- **`origin`** — [AI-Guiders/git-mcp-core](https://github.com/AI-Guiders/git-mcp-core) на GitHub (канон для субмодуля в meta-repo `open`).
- **`github`** — личное зеркало **[KarataevDmitry/git-mcp-core](https://github.com/KarataevDmitry/git-mcp-core)** при необходимости. **Trusted Publishing** пакета `AIGuiders.GitMcp.Core` на nuget.org.

## Layout

- Target: **.NET 10**, **C# 14**.
- No dependency on MCP SDK or Avalonia — only argv shapes and validation messages (`GitArgsResult`).

## Consumers

- **NuGet (рекомендуется):** пакет [`AIGuiders.GitMcp.Core`](https://www.nuget.org/packages/AIGuiders.GitMcp.Core) — `PackageReference` в `git-mcp` и Cascade IDE; отдельный checkout исходников для сборки не нужен после публикации на nuget.org.
- **Исходники:** публичный репозиторий **[KarataevDmitry/git-mcp-core](https://github.com/KarataevDmitry/git-mcp-core)** (зеркало GitLab); `ProjectReference` на `GitMcp.Core.csproj` или субмодуль в meta-repo `open`.

## Публикация на nuget.org (Trusted Publishing)

Инструкция NuGet: [Trusted Publishing — GitHub Actions](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing#github-actions-setup). В форме: owner **`KarataevDmitry`**, repository **`git-mcp-core`**, workflow file **`nuget-publish.yml`** (только имя файла). Workflow в репо: **`.github/workflows/nuget-publish.yml`**; запуск — тег **`v*`** или **Actions → Publish to NuGet → workflow_dispatch**.

## Build

```bash
dotnet build
```

## Tests

Unit tests for `GitCommandBuilder` live in the **git-mcp** repository (`GitMcp.Tests`).
