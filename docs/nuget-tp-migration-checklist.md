# NuGet Trusted Publishing — миграция на guiders-core

После **первого успешного** `release` из `AI-Guiders/guiders-core` удали старые политики на nuget.org (иначе тег на deprecated-репо всё ещё сможет пушить пакет).

## Новая политика (оставить)

| Owner | Repository | Workflow |
|-------|------------|----------|
| `AI-Guiders` | `guiders-core` | `release.yml` |

## Старые политики (удалить)

| Owner | Repository | Workflow (типичный) | Пакет(ы) |
|-------|------------|---------------------|----------|
| `AI-Guiders` | `cdp-core` | `nuget-publish.yml` | `AIGuiders.Cdp.Core` |
| `AI-Guiders` | `cdp-scriptable-ide` | `nuget-publish.yml` | `AIGuiders.Cdp.ScriptableIde` |
| `AI-Guiders` | `cdp-evidence` | *(свой)* | `AIGuiders.Cdp.Evidence` |
| `AI-Guiders` | `agent-notes-core` | *(свой)* | `AIGuiders.AgentNotes.Core` |
| `AI-Guiders` | `agent-task-knowledge-core` | *(свой)* | `AIGuiders.AgentTaskKnowledge.Core` |
| `AI-Guiders` | `agent-findings-core` | *(свой)* | `AIGuiders.AgentFindings.Core` |
| `AI-Guiders` | `agent-failures-core` | *(свой)* | `AIGuiders.AgentFailures.Core` |
| `AI-Guiders` | `dotnet-debug-core` | *(свой)* | `AIGuiders.DotnetDebugMCP.Core` |
| `AI-Guiders` | `dotnet-build-test-core` | *(свой)* | `AIGuiders.DotNetBuildTest.Core` |
| `AI-Guiders` / `KarataevDmitry` | `dotnet-build-test-parsers` | `nuget-publish.yml` | `AIGuiders.DotNetBuildTestParsers` |
| `AI-Guiders` | `roslyn-mcp-core` | `publish-nuget.yml` | `AIGuiders.RoslynMcp.Core` |
| `AI-Guiders` / `KarataevDmitry` | `git-mcp-core` | `nuget-publish.yml` | `AIGuiders.GitMcp.Core` |
| `AI-Guiders` | `hybrid-codebase-index-core` | `publish-nuget.yml` | `AIGuiders.HybridCodebaseIndex.Core` |
| `AI-Guiders` | `terminal-mcp-core` | *(если был)* | `AIGuiders.TerminalMcp.Core` |

> **Примечание:** `KarataevDmitry/git-mcp-core` на GitHub уже редиректит в `AI-Guiders/git-mcp-core`, но запись на nuget.org могла остаться со старым owner — проверь UI.

## Platform (не трогать)

| Owner | Repository | Workflow |
|-------|------------|----------|
| `AI-Guiders` | `guiders-platform` | `release.yml` |

Отдельная политика, не конфликтует с Core.
