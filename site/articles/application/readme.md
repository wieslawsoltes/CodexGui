---
title: "Application Shell"
---

# Application Shell

CodexGui is split into a desktop application and a transport/client library, with Markdown rendering supplied by the ProMarkdown submodule.

## Primary projects

- `src/CodexGui.App` hosts the Avalonia shell, MVVM view models, services, and desktop entry point.
- `src/CodexGui.AppServer` manages protocol DTO generation and JSON-RPC transport for local or remote connections.
- `external/ProMarkdown` provides the `ProMarkdown` control and rendering services consumed by the app.

## Read next

- [Shell and Transport](shell-and-transport/)
- [Interaction Model](interaction-model/)
