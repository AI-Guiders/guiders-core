# AIGuiders.AgentFindings.Core

Workspace-local **artifact-memo + task-DAG journal** (JSONL, hash-gated). Workflow store — not KB, not chat transcript, not project-card/ADR.

Default paths:
- `{workspace}/.agent-findings/memos.jsonl`
- `{workspace}/.agent-findings/tasks.jsonl`

Override relative dir: env `AGENT_FINDINGS_DIR` (single relative name only).

**Model:** project card / ADR / KB = environment TO BE. This store = local AS IS + task TO BE + why (implementation pass).

## API

- `UpsertMemo` / `ListMemos` / `Check` / `GetMemo` — file memos; `Check` advice includes `stale_deps`
- `UpsertTask` / `ListTasks` / `GetTask` — task DAG with `effectiveStatus` from `blocked_by`

## Consumers

- **[agent-findings-mcp](../agent-findings-mcp)** — thin MCP

## License

[MIT](LICENSE) · [declaration](https://github.com/AI-Guiders/licensing/blob/main/docs/ethical-use.md)
