> **Former repo** — имя удалённого standalone-репо (GitHub, 2026-08-23). SSOT: этот monorepo.

| Folder | NuGet ID (typical) | Former repo |
|--------|-------------------|-------------|
| `src/Cdp.Core` | `AIGuiders.Cdp.Core` | cdp-core |
| `src/Cdp.ScriptableIde` | `AIGuiders.Cdp.ScriptableIde` | cdp-scriptable-ide |
| `src/Cdp.PackageIntelligence` | `AIGuiders.Cdp.PackageIntelligence` | (new) |
| `src/Cdp.Evidence` | `AIGuiders.Cdp.Evidence` | cdp-evidence |
| `src/Cdp.Ignite.Client` | `AIGuiders.Cdp.Ignite.Client` | cdp-ignite-client |
| `src/Cdp.Lsp.Core` | `AIGuiders.Cdp.Lsp.Core` | lsp-lang |
| `src/AgentNotes.Core` | `AIGuiders.AgentNotes.Core` | agent-notes-core |
| `src/AgentNotes.Mcp.Hosting` | (library, no standalone nupkg) | agent-notes-mcp |
| `src/AgentTaskKnowledge.Core` | `AIGuiders.AgentTaskKnowledge.Core` | agent-task-knowledge-core |
| `src/AgentFindings.Core` | `AIGuiders.AgentFindings.Core` | agent-findings-core |
| `src/AgentFailures.Core` | `AIGuiders.AgentFailures.Core` | agent-failures-core |
| `src/DotnetDebug.Core` | `AIGuiders.DotnetDebugMCP.Core` | dotnet-debug-core |
| `src/DotNetBuildTestParsers` | `AIGuiders.DotNetBuildTestParsers` | dotnet-build-test-parsers |
| `src/DotNetBuildTest.Core` | `AIGuiders.DotNetBuildTest.Core` | dotnet-build-test-core |
| `src/RoslynMcp.Core` | `AIGuiders.RoslynMcp.Core` | roslyn-mcp-core |
| `src/GitMcp.Core` | `AIGuiders.GitMcp.Core` | git-mcp-core |
| `src/HybridCodebaseIndex.Core` | `AIGuiders.HybridCodebaseIndex.Core` | hybrid-codebase-index-core |
| `src/TerminalMcp.Core` | `AIGuiders.TerminalMcp.Core` | terminal-mcp-core |
| `src/TypescriptLang.Core` | `AIGuiders.TypescriptLang.Core` | typescript-lang |
| `src/TypescriptLang.Core/worker` | (Node companion, not a NuGet) | typescript-lang/worker |

**Not in Core** (stay product/sibling repos): `ai-native-ui` (Anui.*), MCP host exes, smoke tools under old `cdp-scriptable-ide/tools`.
