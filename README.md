# CodexGui

CodexGui is a native Avalonia desktop client for the Codex app-server protocol. The current shell is intentionally aligned to the Codex desktop app: light workspace chrome, a left thread rail, a center conversation stream, a right document pane, and a bottom terminal strip, now with initial phase-two interaction on top of the real app-server transport.

## Current scope

- `stdio` process transport via `codex app-server` plus remote `ws://` / `wss://` app-server endpoint support for local-network access
- Strict `initialize` and `initialized` handshake
- Snapshot requests for account, models, threads, skills, apps, and admin requirements, with continuous live refresh while connected
- Turn authoring with `thread/start`, `thread/resume`, `turn/start`, and `turn/interrupt`
- Pending approval and tool-input surfaces for command execution, file changes, and dynamic tool calls
- Rich item-specific rendering for commands, diffs, tool calls, and the right-side detail document
- Notification activity feed plus persistent interactive local shell control rendered in a Codex-style terminal strip
- Codex-inspired desktop shell built with Avalonia UI, XAML, and C#

## Run

```bash
dotnet run --project src/CodexGui.App/CodexGui.App.csproj
```

Use the left sidebar connection panel to point the app at either:
- a local Codex executable (default: `codex` with arguments `app-server`)
- a remote WebSocket endpoint (for example `ws://192.168.1.20:7777`)

Current limitations:

- ChatGPT token refresh requests are still rejected explicitly.
- Review mode, auth management, and full parity for all server-request types are still in progress.

## Roadmap

The implementation plan and the phase-two parity backlog are documented in [docs/implementation-plan.md](docs/implementation-plan.md).
