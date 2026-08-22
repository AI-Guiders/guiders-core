# AIGuiders.AgentFailures.Core

Workspace-local **tool failure journal** (JSONL). Workflow store — not KB.

Default: `{workspace}/.agent-failures/{tool}.jsonl`  
Env override: `AGENT_FAILURES_DIR`.

## v0.2

- Categories: `incorrect_invocation` | `missing_precondition` | `environment` | `tool_bug` | `unknown`
- Meta: `projectId`, `app`, `taskId`
- Dedupe: same fingerprint within 15m without new resolution → no append
- Resolution upsert: merge onto prior fingerprint (seenCount unchanged)
- `suggestedNext`: stored or heuristic

## API

- `WorkspaceFailuresStore.Record(...)` → `FailureView` (`Deduped`, `SuggestedNext`)
- `List(..., category, projectId, app, taskId, latestOnly, limit)` → `FailureView[]`
- `Append(...)` — legacy wrapper

## License

[Hippocratic License 2.1](LICENSE) (Ethical Source / SPDX `Hippocratic-2.1`)
