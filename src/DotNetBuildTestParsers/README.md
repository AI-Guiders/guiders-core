# dotnet-build-test-parsers

Вынесенная библиотека парсеров вывода **`dotnet build`** и **`dotnet test`**. Пакет на NuGet: **`AIGuiders.DotNetBuildTestParsers`** (пространство имён остаётся `DotNetBuildTestParsers`).

## Статус

- CI: GitHub Actions `ci.yml` (сборка + тесты).
- Релиз: тег `v*` или ручной запуск `nuget-publish.yml` с вводом версии; публикация через **Trusted Publishing** (OIDC), см. [docs/nuget-trusted-publishing.md](docs/nuget-trusted-publishing.md).

## Разработка

```bash
dotnet build DotNetBuildTestParsers.sln -c Release
dotnet test DotNetBuildTestParsers.sln -c Release --no-build
```

## Потребители

Перевод с `ProjectReference` на пакет:

- [Cascade IDE](https://github.com/KarataevDmitry/cascade-ide) (`CascadeIDE.csproj`).
- [dotnet-build-test-mcp](https://github.com/KarataevDmitry/dotnet-build-test-mcp) (`DotnetBuildTestMcp.csproj`).

Исторический монорепозиторий с копией исходников: `dotnet-build-test-mcp-repo` в workspace `financial-open` — после стабилизации пакета дублирование там можно убрать.
