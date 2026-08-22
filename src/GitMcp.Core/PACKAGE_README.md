# AIGuiders.GitMcp.Core

Библиотека **`GitMcp.Core`** для .NET **10+**: единообразная сборка аргументов **`git`** (списки строк для `ProcessStartInfo.ArgumentList` или аналога). Общий слой для **[git-mcp](https://github.com/KarataevDmitry/git-mcp)** (MCP) и **Cascade IDE** — паритет сценариев `status` / `diff` / `commit` / `push` и др. ([ADR 0019](https://github.com/KarataevDmitry/cascade-ide/blob/main/docs/adr/0019-shared-git-core-ide-and-git-mcp.md) в репозитории IDE).

Без зависимостей от MCP SDK и Avalonia — только примитивы и тип **`GitArgsResult`** для ошибок валидации argv.

## Установка

```bash
dotnet add package AIGuiders.GitMcp.Core
```

**Ссылки:** [NuGet.org](https://www.nuget.org/packages/AIGuiders.GitMcp.Core) · [Исходники](https://github.com/KarataevDmitry/git-mcp-core) · лицензия [MIT](https://github.com/KarataevDmitry/git-mcp-core/blob/main/LICENSE).

## Пример

```csharp
using GitMcp.Core;

// Те же argv, что у панели Git в IDE
IReadOnlyList<string> argv = GitCommandBuilder.StatusShortBranch();
// => "git", "status", "--short", "--branch"

// Последовательность как в MCP git_status
foreach (var step in GitCommandBuilder.StatusMcpSequence())
{
    // step — список аргументов после "git"
}
```

Полный API — **`GitCommandBuilder`**, **`GitWorkTree`**, **`GitPreflight`**; подробности в XML-комментариях в репозитории.

## Сборка из исходников (maintainers)

```bash
dotnet pack GitMcp.Core.csproj -c Release -o nupkg
```

Публикация на **nuget.org**: GitHub Actions + [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), workflow **`nuget-publish.yml`** в репозитории [git-mcp-core](https://github.com/KarataevDmitry/git-mcp-core).
