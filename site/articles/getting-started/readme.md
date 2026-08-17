---
title: "Getting Started"
---

# Getting Started

CodexGui is a .NET 10 solution centered around an Avalonia desktop client. The fastest way to get productive is:

1. initialize the ProMarkdown submodule
2. build the solution from the repository root
3. run the desktop app
4. connect to a local `codex app-server` process or a remote WebSocket endpoint

## Start here

- [Overview](overview/) for the current feature set and connection model.
- [Running the App](running-the-app/) for the exact local commands.

## Core entry points

- Main shell: `src/CodexGui.App`
- App-server client: `src/CodexGui.AppServer`
- Markdown dependency: `external/ProMarkdown`
- Solution file: `CodexGui.slnx`
