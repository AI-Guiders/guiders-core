# AIGuiders.DotNetBuildTest.Core

Job coordinator and structured `dotnet build` / `dotnet test` / `dotnet publish` execution for **dotnet-build-test-mcp** and **Cascade IDE**.

- Queue with cancel, timeout, and rolling log
- JSON results (errors, test counts) via **AIGuiders.DotNetBuildTestParsers**
- No MCP dependency — host supplies stdio or in-process wiring

Source: [github.com/AI-Guiders/dotnet-build-test-core](https://github.com/AI-Guiders/dotnet-build-test-core)
