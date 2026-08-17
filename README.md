# CodexGui

Native Avalonia desktop client for the Codex app-server protocol.

CodexGui combines a typed app-server transport, an interactive Avalonia shell, and rich Markdown rendering supplied by the [ProMarkdown](https://github.com/wieslawsoltes/ProMarkdown) submodule.

> Status: active development. The current codebase supports interactive sessions, turn authoring, pending approvals, rich item detail panes, and both local `stdio` and remote WebSocket app-server connections.

## Highlights

- Native Avalonia shell with navigation, thread, conversation, detail, and terminal surfaces.
- Local `codex app-server` process transport plus remote `ws://` and `wss://` endpoints.
- Typed protocol models generated from OpenAPI with strict initialization lifecycle handling.
- Thread, turn, interrupt, approval, and user-input workflows.
- Rich conversation Markdown rendered through the pinned ProMarkdown source dependency.

## Repository map

| Path | Responsibility |
| --- | --- |
| `src/CodexGui.App` | Avalonia desktop application, shell, MVVM view models, and session orchestration. |
| `src/CodexGui.AppServer` | Typed JSON-RPC transport and protocol models for local and remote app-server connections. |
| `external/ProMarkdown` | Git submodule providing the Markdown control and rendering services used by the app. |
| `site` | Lunet documentation site. |

The ProMarkdown libraries, plugins, sample, tests, and implementation documentation are maintained in the [ProMarkdown repository](https://github.com/wieslawsoltes/ProMarkdown).

## NuGet packages

| Package | Version | Downloads | Notes |
| --- | --- | --- | --- |
| [`CodexGui.App`](https://www.nuget.org/packages/CodexGui.App) | ![NuGet Version](https://img.shields.io/nuget/v/CodexGui.App?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/CodexGui.App?logo=nuget) | .NET tool package for launching the desktop client through `codexgui`. |
| [`CodexGui.AppServer`](https://www.nuget.org/packages/CodexGui.AppServer) | ![NuGet Version](https://img.shields.io/nuget/v/CodexGui.AppServer?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/CodexGui.AppServer?logo=nuget) | Typed app-server client transport and protocol models. |

## Tech stack

- .NET 10
- Avalonia 12
- CommunityToolkit.Mvvm
- NSwag-generated protocol DTOs
- ProMarkdown

## Getting started

### Prerequisites

- .NET 10 SDK
- A local `codex` executable on `PATH` when using the default process transport

### Clone with dependencies

```bash
git clone --recurse-submodules https://github.com/wieslawsoltes/CodexGui.git
cd CodexGui
```

For an existing clone:

```bash
git submodule update --init --recursive
```

### Build and run

```bash
dotnet build CodexGui.slnx
dotnet run --project src/CodexGui.App/CodexGui.App.csproj
```

The default connection launches `codex app-server`. The connection settings also accept remote `ws://` and `wss://` endpoints.

## Documentation

```bash
dotnet tool restore
bash ./check-docs.sh
bash ./serve-docs.sh
```

PowerShell equivalents are included for the documentation commands.

## CI and releases

- `.github/workflows/build.yml` initializes submodules, builds CodexGui, validates docs, and packs CodexGui packages.
- `.github/workflows/docs.yml` publishes the Lunet site.
- `.github/workflows/release.yml` builds and publishes only the CodexGui app and app-server packages.

## Roadmap

CodexGui remains focused on deeper Codex app parity, including review workflows, thread rollback and forking polish, authentication management, app and skill administration, and remaining server-request types. See the [roadmap](site/articles/reference/roadmap.md).

## License

MIT. See [`LICENSE`](LICENSE).
