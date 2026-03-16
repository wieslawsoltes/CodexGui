---
title: "Overview"
---

# Overview

CodexGui is a native Avalonia desktop client for the Codex app-server protocol.

The current codebase already includes:

- a Codex-style shell with a navigation rail, thread list, conversation surface, detail pane, and terminal strip
- local `stdio` transport using `codex app-server`
- remote `ws://` and `wss://` transport support
- strict `initialize` / `initialized` connection handling
- turn authoring, thread start/resume, interrupts, and pending approval surfaces
- reusable markdown rendering and editing packages with multiple feature plugins

## Connection model

By default, the application starts with `codex` as the command path and `app-server` as the arguments. The connection panel also supports pointing directly at a remote app-server endpoint.

## What to read next

- [Running the App](running-the-app/)
- [Shell and Transport](../application/shell-and-transport/)
- [Plugin Ecosystem](../markdown/plugin-ecosystem/)
