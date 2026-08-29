# Публикация на nuget.org (Trusted Publishing)

Долгоживущий API key **не нужен**: [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishers) + GitHub OIDC (`NuGet/login@v1`).

## 1. Политика на nuget.org (один раз)

1. Войти на [nuget.org](https://www.nuget.org/) (**LonelySoul**).
2. **Account settings** → **Trusted publishers** → **Add**.
3. GitHub:
   - **Repository owner:** `AI-Guiders`
   - **Repository:** `guiders-core`
   - **Workflow file:** `release.yml` (только имя файла)
   - **Environment:** пусто

Одна политика покрывает **все** пакеты `AIGuiders.*` из этого workflow (как `guiders-platform` + `release.yml`).

### Снять устаревшие политики

После первого успешного publish из `guiders-core` **удали** старые Trusted Publishers, привязанные к `*-core` репо. Полный чеклист: [nuget-tp-migration-checklist.md](nuget-tp-migration-checklist.md).

## 2. Версии пакетов

Версия берётся из `<Version>` в каждом `src/*/*.csproj` — **не** из тега.

Перед релизом bump только те пакеты, которые реально публикуешь. Тег `v*` — триггер CI; `--skip-duplicate` пропускает уже опубликованные версии.

## 3. Запуск

```bash
# после bump Version в нужных csproj:
git add src/
git commit -m "chore(release): bump core package versions"
git tag v2026.08.22-core
git push origin main --tags
```

Или **Actions → release → Run workflow** (без тега).

## 4. Локальная проверка

```bash
dotnet pack -c Release --output ./artifacts
# список nupkg:
Get-ChildItem artifacts/*.nupkg | ForEach-Object { $_.Name }
```

### Symbols (snupkg)

`scripts/nuget/push-artifacts.sh` (вызывается из `release.yml`) пушит `.snupkg` **только если** соответствующий `.nupkg` реально загружен в этом прогоне (не `--skip-duplicate`). Иначе NuGet.org отклоняет symbols: PDB не совпадает с DLL уже опубликованного nupkg ([NuGet/Home#10475](https://github.com/NuGet/Home/issues/10475)).

Починить failed symbols для уже опубликованной версии:

1. Checkout **того же коммита**, что опубликовал nupkg (тег релиза).
2. `dotnet pack src/Cdp.Core/Cdp.Core.csproj -c Release -o ./artifacts`
3. `dotnet nuget push ./artifacts/AIGuiders.Cdp.Core.{version}.snupkg` (nupkg не трогать).

Или bump `<Version>` и выпустить пару nupkg+snupkg заново.

Packable-проекты: 17 пакетов (см. [packages-inventory.md](packages-inventory.md)). `AgentNotes.Mcp.Hosting` — `IsPackable=false`.

## 5. Проверка после CI

- Страницы пакетов на nuget.org
- `dotnet add package AIGuiders.Cdp.Core -v {версия}` из чистого проекта
