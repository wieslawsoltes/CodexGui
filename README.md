# CodexGui

Native Avalonia desktop client for the Codex app-server protocol.

CodexGui brings a Codex-style workspace to a desktop-native .NET application. The repository combines a typed app-server transport, an interactive Avalonia shell, and a modular markdown rendering stack for rich conversation, diff, and document surfaces.

> Status: active development. The current codebase already supports interactive sessions, turn authoring, pending approvals, rich item detail panes, and both local `stdio` and remote WebSocket app-server connections.

## Highlights

- Native Avalonia shell with a left navigation rail, thread sidebar, center conversation stream, right detail pane, and bottom terminal strip.
- Local process transport using `codex app-server`, plus remote `ws://` and `wss://` app-server endpoint support.
- Strict `initialize` / `initialized` lifecycle handling with typed protocol models generated from OpenAPI via NSwag.
- Live workspace snapshots for accounts, models, threads, skills, apps, and runtime requirements.
- Turn workflows built on `thread/start`, `thread/resume`, `turn/start`, and `turn/interrupt`.
- Interactive approval and user-input surfaces for commands, file changes, and dynamic tool calls.
- Rich markdown-based detail rendering backed by a reusable plugin ecosystem for alerts, figures, footers, math, Mermaid diagrams, syntax highlighting, and more.

## Solution map

| Project | Responsibility |
| --- | --- |
| `src/CodexGui.App` | Main Avalonia desktop application, shell layout, MVVM view models, and session orchestration. |
| `src/CodexGui.AppServer` | Typed JSON-RPC transport layer for local process and remote WebSocket connections. |
| `src/CodexGui.Markdown` | Core markdown rendering and editing services built on Avalonia and Markdig. |
| `src/CodexGui.Markdown.Plugin.*` | Feature plugins for alerts, custom containers, definition lists, figures, footers, math, Mermaid, syntax highlighting, and TextMate integration. |
| `src/CodexGui.Markdown.Sample` | Standalone sample application for exploring the markdown stack in isolation. |

## Tech stack

- .NET 10
- Avalonia 11
- CommunityToolkit.Mvvm
- NSwag-generated protocol DTOs
- Markdig-based markdown pipeline

## Getting started

### Prerequisites

- .NET 10 SDK
- A local `codex` executable on your `PATH` if you want to use the default process transport

### Build the solution

```bash
dotnet build CodexGui.slnx
```

### Run the desktop app

```bash
dotnet run --project src/CodexGui.App/CodexGui.App.csproj
```

When the app opens, the default connection settings target a local `codex app-server` process. You can also point the connection panel at a remote `ws://` or `wss://` endpoint if you already have an app-server running elsewhere.

### Run the markdown sample

```bash
dotnet run --project src/CodexGui.Markdown.Sample/CodexGui.Markdown.Sample.csproj
```

Use the sample app to inspect the reusable markdown editor and preview pipeline outside the main Codex workspace shell.

## Documentation

Project documentation now lives under `site/` and is built with Lunet for GitHub Pages deployment.

### Build docs locally

```bash
dotnet tool restore
bash ./check-docs.sh
```

### Serve docs locally

```bash
bash ./serve-docs.sh
```

The repository also includes PowerShell equivalents: `./build-docs.ps1`, `./check-docs.ps1`, and `./serve-docs.ps1`.

### GitHub Actions

- `.github/workflows/build.yml` validates the solution build, test baseline, Lunet docs generation, and NuGet package creation.
- `.github/workflows/docs.yml` deploys the `site/` output to GitHub Pages.
- `.github/workflows/release.yml` builds tagged releases, packs the reusable libraries, publishes NuGet packages, and creates the GitHub release entry.

## What ships today

- Codex-style desktop workspace chrome and thread inspection flows
- Live protocol notification rendering and transport activity feed
- Rich center-pane cards for commands, diffs, plans, reasoning, and tool-call items
- Right-pane detail rendering driven by the selected conversation item
- Pending approval cards for command execution, file changes, and dynamic tool interactions
- Modular markdown rendering infrastructure that can be reused independently of the main app

## Roadmap

CodexGui is currently in the parity-building phase. The implemented shell and transport foundations are in place, while the remaining work is focused on deeper Codex app parity.

Current gaps called out in the repository include:

- review mode workflows
- thread forking and rollback polish
- auth management and ChatGPT token refresh handling
- broader app and skill administration surfaces
- full parity for remaining server-request types

For the active roadmap and design direction, see [`docs/implementation-plan.md`](docs/implementation-plan.md). The `docs/` directory also contains detailed markdown subsystem plans and feature notes.

## License

MIT. See [`LICENSE`](LICENSE).
