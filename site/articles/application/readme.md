---
title: "Application Shell"
---

# Application Shell

CodexGui is split into a desktop application, a transport/client library, and a reusable markdown subsystem.

## Primary projects

- `src/CodexGui.App` hosts the Avalonia shell, MVVM view models, services, and desktop entry point.
- `src/CodexGui.AppServer` manages protocol DTO generation and JSON-RPC transport for local or remote connections.
- `src/CodexGui.Markdown` and `src/CodexGui.Markdown.Plugin.*` provide the document rendering stack used by the shell and the sample app.

## Read next

- [Shell and Transport](shell-and-transport/)
- [Interaction Model](interaction-model/)
