---
title: "Getting Started"
---

# Getting Started

CodexGui is a .NET 10 solution centered around an Avalonia desktop client. The fastest way to get productive is:

1. build the solution from the repository root
2. run the desktop app or the markdown sample
3. connect to a local `codex app-server` process or a remote WebSocket endpoint
4. use the docs and source tree together as you inspect or extend the application

## Start here

- [Overview](overview/) for the current feature set and connection model.
- [Running the App](running-the-app/) for the exact local commands.

## Core entry points

- Main shell: `src/CodexGui.App`
- App-server client: `src/CodexGui.AppServer`
- Markdown libraries and sample: `src/CodexGui.Markdown*`
- Solution file: `CodexGui.slnx`
