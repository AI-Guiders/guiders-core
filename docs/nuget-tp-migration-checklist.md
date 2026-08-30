# NuGet Trusted Publishing — миграция на guiders-core

После **первого успешного** `release` из `AI-Guiders/guiders-core` удали старые политики на nuget.org (иначе orphaned TP-записи остаются в UI).

> **2026-08-23:** legacy GitHub-репозитории из таблицы «удалить» **удалены** (не archived).  
> **2026-08-30:** повторная проверка `gh repo view` — все `*-core` из списка → **NOT_FOUND**. Код и pack — только `guiders-core`.

---

## Аудит 2026-08-30

### GitHub (проверено)

| Статус | Репозитории |
|--------|-------------|
| **Удалены** | `cdp-core`, `cdp-scriptable-ide`, `cdp-evidence`, `agent-notes-core`, `agent-task-knowledge-core`, `agent-findings-core`, `agent-failures-core`, `dotnet-debug-core`, `dotnet-build-test-core`, `dotnet-build-test-parsers`, `roslyn-mcp-core`, `git-mcp-core`, `hybrid-codebase-index-core`, `terminal-mcp-core`, `typescript-lang`, `AIGuiders.AgentNotes.Core`, `roslyn-mcp`, `dotnet-build-test-mcp-repo` |
| **Живые monorepo** | `guiders-core`, `guiders-platform`, `guiders-ui-platform`, `guiders-plugin-host` |
| **Живые, NuGet отдельно** (не в core) | `mcp-tool-manifest`, `webcam-mcp-shared`, `dotnet-mcp-templates`, `AIGuiders.DotnetTools` |
| **Живые, MCP exe only** (NuGet core в guiders-core) | `hybrid-codebase-index`, `dotnet-debug-mcp`, `git-mcp`, `agent-notes-mcp`, … |

### nuget.org (только вручную в UI)

API для списка Trusted Publishers нет. Открой **Account → Trusted publishers** под `LonelySoul` и под **`AIGuiders`** (после миграции) и сверь с таблицами ниже.

**Если ты уже удаляла строки из «удалить»** — для merge в `guiders-core` больше ничего не нужно, кроме проверки дубликатов `KarataevDmitry` / `LonelySoul` на те же repo.

---

## Оставить (активные политики)

| GitHub owner | Repository | Workflow | Пакеты |
|--------------|------------|----------|--------|
| `AI-Guiders` | `guiders-core` | `release.yml` | все `AIGuiders.*` **кроме** Platform / UI / PluginHost / DotnetTools / McpToolManifest / WebcamMcp (см. inventory) |
| `AI-Guiders` | `guiders-platform` | `release.yml` | `AIGuiders.Platform.*` |
| `AI-Guiders` | `guiders-ui-platform` | `release.yml` | `AIGuiders.UI.*` |
| `AI-Guiders` | `guiders-plugin-host` | `release.yml` | `AIGuiders.PluginHost.*` |
| `AI-Guiders` | `mcp-tool-manifest` | `nuget-publish.yml` | `AIGuiders.McpToolManifest` — **не** в guiders-core |
| `AI-Guiders` | `webcam-mcp-shared` | `nuget-publish.yml` | `AIGuiders.WebcamMcp.Shared` — **не** в guiders-core |
| `AI-Guiders` | `dotnet-mcp-templates` | `publish.yml` | `AIGuiders.DotnetMcp.Templates` |
| `AI-Guiders` | `AIGuiders.DotnetTools` | `publish.yml` | `AIGuiders.Cli`, `AIGuiders.DotnetTools.*` |

Целевой **Package owner** политики: `AIGuiders` (после миграции с `LonelySoul`). Workflow `user:` в YAML должен совпадать с аккаунтом, где создана политика.

---

## Удалить (orphan — репо на GitHub нет, пакет в guiders-core)

Если любая из строк **ещё** есть в Trusted publishers — удалить:

| Repository (мертвый) | Бывший пакет | Сейчас |
|----------------------|--------------|--------|
| `cdp-core` | `AIGuiders.Cdp.Core` | `guiders-core` |
| `cdp-scriptable-ide` | `AIGuiders.Cdp.ScriptableIde` | `guiders-core` |
| `cdp-evidence` | `AIGuiders.Cdp.Evidence` | `guiders-core` |
| `agent-notes-core` | `AIGuiders.AgentNotes.Core` | `guiders-core` |
| `agent-task-knowledge-core` | `AIGuiders.AgentTaskKnowledge.Core` | `guiders-core` |
| `agent-findings-core` | `AIGuiders.AgentFindings.Core` | `guiders-core` |
| `agent-failures-core` | `AIGuiders.AgentFailures.Core` | `guiders-core` |
| `dotnet-debug-core` | `AIGuiders.DotnetDebugMCP.Core` | `guiders-core` |
| `dotnet-build-test-core` | `AIGuiders.DotNetBuildTest.Core` | `guiders-core` |
| `dotnet-build-test-parsers` | `AIGuiders.DotNetBuildTestParsers` | `guiders-core` |
| `roslyn-mcp-core` | `AIGuiders.RoslynMcp.Core` | `guiders-core` |
| `git-mcp-core` | `AIGuiders.GitMcp.Core` | `guiders-core` |
| `hybrid-codebase-index-core` | `AIGuiders.HybridCodebaseIndex.Core` | `guiders-core` |
| `terminal-mcp-core` | `AIGuiders.TerminalMcp.Core` | `guiders-core` |
| `typescript-lang` | `AIGuiders.TypescriptLang.Core` | `guiders-core` |

Проверь **дубликаты**: тот же repo под owner `KarataevDmitry` или старый workflow — тоже удалить.

---

## Удалить (репо живо, но NuGet больше не оттуда)

| Repository | Почему удалить TP |
|------------|-------------------|
| `hybrid-codebase-index` | Только `release-mcp.yml` → ZIP MCP; **нет** push NuGet. Core — `guiders-core`. |
| `roslyn-mcp` | Репо удалён; если политика осталась — orphan. |

---

## Не мерджить в guiders-core (осознанно отдельно)

| Пакет | Репо | Решение |
|-------|------|---------|
| `AIGuiders.McpToolManifest` | `mcp-tool-manifest` | Оставить отдельный репо + TP |
| `AIGuiders.WebcamMcp.Shared` | `webcam-mcp-shared` | Оставить |
| `AIGuiders.DotnetMcp.Templates` | `dotnet-mcp-templates` | Оставить |
| `AIGuiders.Cli` / DotnetTools | `AIGuiders.DotnetTools` | Оставить |

Опциональный будущий merge в `guiders-core` — только если хочешь один `release.yml` на всё; выигрыш небольшой (4 маленьких пакета).

---

## Platform / UI / PluginHost

Не часть `guiders-core` — отдельные политики (не удалять):

| Repository | Workflow |
|------------|----------|
| `guiders-platform` | `release.yml` |
| `guiders-ui-platform` | `release.yml` |
| `guiders-plugin-host` | `release.yml` |

---

## Быстрая самопроверка в nuget.org

1. Под **каждым** аккаунтом (`LonelySoul`, `AIGuiders`): Trusted publishers → нет строк с repo из таблицы «удалить (orphan)».
2. Есть ровно **одна** политика на `guiders-core` + `release.yml` (на целевом owner).
3. `mcp-tool-manifest` / `webcam-mcp-shared` / `dotnet-mcp-templates` / `AIGuiders.DotnetTools` — политики **на месте** (если ещё публикуете оттуда).
4. Тест без новой версии: **Actions → guiders-core → release → Run workflow** → шаг **NuGet login** зелёный.

См. также [nuget-publishing.md](nuget-publishing.md), pain **N-022** в [ANPM inventory](https://github.com/AI-Guiders/agent-nuget-pm/blob/main/docs/ANPM-pain-inventory.md).
