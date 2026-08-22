# dotnet-build-test-core

Shared **job coordinator**, **dotnet CLI runner**, and **structured build/test/publish** results for:

- [dotnet-build-test-mcp](https://github.com/AI-Guiders/dotnet-build-test-mcp) (stdio MCP adapter)
- Cascade IDE **AEE** (in-process via `BuildTestJobService` / `BuildTestJobCoordinator`)

NuGet: **[AIGuiders.DotNetBuildTest.Core](https://www.nuget.org/packages/AIGuiders.DotNetBuildTestCore)** (after first publish).

Depends on **[AIGuiders.DotNetBuildTestParsers](https://www.nuget.org/packages/AIGuiders.DotNetBuildTestParsers)** for MSBuild/test output parsing.

## Public API (v0.1)

| Type | Role |
|------|------|
| `BuildTestJobService` | Facade for MCP tool handlers (JSON in/out) |
| `BuildTestJobCoordinator` | Queue, cancel, logs, structured results |
| `DotnetProcessRunner` | `dotnet` subprocess with timeout/cancel |
| `DotnetCommandBuilder` | argv for build/test/publish |
| `SolutionOrProjectPathResolver` | `.sln` / `.slnx` / `.csproj` resolution |
| `DotnetExecutionOptions` | Configuration, filter, no-build, … |

## Build

```bash
dotnet test DotNetBuildTest.Core.sln -c Release
dotnet pack DotNetBuildTest.Core.csproj -c Release -o nupkg
```

## Publish (NuGet Trusted Publishing)

See [docs/nuget-trusted-publishing.md](docs/nuget-trusted-publishing.md).
