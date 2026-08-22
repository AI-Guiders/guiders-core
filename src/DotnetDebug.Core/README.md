# DotnetDebug.Core (`AIGuiders.DotnetDebugMCP.Core`)

Общее ядро для **dotnet-debug-mcp** и IDE: DAP-клиент (netcoredbg), хелперы DAP, хранение брейкпоинтов (`.dotnet-debug-mcp-breakpoints.json`).

- **Карточка на NuGet** показывает только потребительское описание — файл [`PACKAGE_README.md`](PACKAGE_README.md) (подключается в nupkg через `PackageReadmeFile`).
- **Публикация пакета** (Trusted Publishing, workflow) — отдельно: [`docs/nuget-publishing.md`](docs/nuget-publishing.md).

## Сборка и pack

```bash
dotnet build DotnetDebug.Core.csproj -c Release
dotnet pack DotnetDebug.Core.csproj -c Release -o ./out
```

## Связанные репозитории

- [dotnet-debug-core](https://github.com/KarataevDmitry/dotnet-debug-core) (этот пакет)
- [dotnet-debug-mcp](https://github.com/KarataevDmitry/dotnet-debug-mcp) (MCP-сервер поверх этого ядра)
