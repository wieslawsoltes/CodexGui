---
title: "Running the App"
---

# Running the App

## Prerequisites

- .NET 10 SDK
- a local `codex` executable on `PATH` if you want to use the default process transport

## Build the solution

```bash
dotnet build CodexGui.slnx
```

## Run the desktop client

```bash
dotnet run --project src/CodexGui.App/CodexGui.App.csproj
```

When the window opens, the default connection settings target `codex app-server`. You can replace that with a remote `ws://` or `wss://` endpoint in the connection sidebar.

## Run the markdown sample

```bash
dotnet run --project src/CodexGui.Markdown.Sample/CodexGui.Markdown.Sample.csproj
```

The sample app is useful when you want to work on markdown rendering or editor behavior without running the full Codex shell.

## Local documentation site

```bash
dotnet tool restore
bash ./serve-docs.sh
```

The generated site uses Lunet and serves from the repository's `site/` folder.
