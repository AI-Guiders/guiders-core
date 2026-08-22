# AIGuiders.DotnetDebugMCP.Core

Библиотека на **.NET 10**: общий слой для [dotnet-debug-mcp](https://github.com/KarataevDmitry/dotnet-debug-mcp) и IDE (Cascade IDE) — обмен с отладчиком по протоколу **DAP** (stdio, Content-Length + JSON-RPC), утилиты для дерева переменных и хранение брейкпоинтов на диске.

**Лицензия:** MIT (см. `LICENSE` в репозитории).  
**Исходники:** [github.com/KarataevDmitry/dotnet-debug-core](https://github.com/KarataevDmitry/dotnet-debug-core)

---

## Зачем этот пакет

- Запуск **netcoredbg** (или другого адаптера с DAP по stdio) и минимальный клиент запросов/ответов без привязки к конкретному UI.
- Один код для **MCP-сервера отладки** и для **настольного приложения**, чтобы не дублировать DAP и файл брейкпоинтов.

---

## Установка

```bash
dotnet add package AIGuiders.DotnetDebugMCP.Core
```

Актуальную версию смотри на [nuget.org/packages/AIGuiders.DotnetDebugMCP.Core](https://www.nuget.org/packages/AIGuiders.DotnetDebugMCP.Core/).

---

## Публичный API (кратко)

| Тип | Назначение |
|-----|------------|
| **DapClient** | Клиент DAP: процесс адаптера, stdio, Content-Length, JSON-RPC, колбэки событий и обрыва связи. |
| **DapShared** | Общие константы и хелперы для DAP. |
| **DapFrameInspection** | Markdown/JSON стека и переменных кадра после stopped. |
| **DapStopContext** | Один снимок stop-context (meta + stack + variables) для MCP и in-proc IDE. |
| **DapFrameInspectionOptions** / **DapStopContextMeta** | Параметры раскрытия и метаданные остановки. |
| **DapVariableExpansion** | Раскрытие переменных DAP в структуру для UI/дерева. |
| **DapVariableDescriptor** | Дескриптор узла переменной (readonly record struct). |
| **DapVariableTreeNode** | Узел дерева переменных. |
| **BreakpointsStorage** | Сериализация брейкпоинтов в JSON рядом с решением (в т.ч. `.dotnet-debug-mcp-breakpoints.json`). |

Подробности — в XML-комментариях в исходниках.

---

## Сборка и тест локально

```bash
dotnet build DotnetDebug.Core.csproj -c Release
dotnet pack DotnetDebug.Core.csproj -c Release -o ./out
```
